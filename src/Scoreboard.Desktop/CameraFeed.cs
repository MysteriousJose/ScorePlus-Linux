using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using Scoreboard.Core;
using Size = OpenCvSharp.Size;

namespace Scoreboard.Desktop;

/// <summary>
/// A live camera feed, shared by every scorebug video element watching it. Frames are captured on
/// a background thread and handed to the UI already frozen, so a 1080p preview never fights the
/// dispatcher. While the feed runs it also records short clips to disk; <see cref="TakeReplay"/>
/// closes the current clip and returns it, which is what the Replay button plays on the scorebug.
/// </summary>
public sealed class CameraFeed : IDisposable
{
    // One capture per device index: opening the same webcam twice fails on most drivers, and two
    // scorebug elements showing the camera should share the one feed.
    static readonly Dictionary<int, CameraFeed> Feeds = [];

    // The capture device's own default resolution is used on purpose: forcing 720p on a webcam
    // that only delivers 5 fps at that size makes the scorebug look frozen. The rate also matches
    // the recorder, so an instant replay plays back at real speed rather than fast-forward.
    const double FramesPerSecond = 15;
    static readonly TimeSpan SegmentLength = TimeSpan.FromSeconds(120);

    readonly int _index;
    readonly Dispatcher _dispatcher;
    readonly object _gate = new();
    Thread? _thread;
    volatile bool _wanted;
    volatile bool _framePending;
    VideoWriter? _writer;
    string? _segmentPath;
    DateTime _segmentStarted;
    bool _recordingFailed;

    CameraFeed(int index, Dispatcher dispatcher)
    {
        _index = index;
        _dispatcher = dispatcher;
    }

    public static CameraFeed Get(int index)
    {
        var key = Math.Max(0, index);
        if (Feeds.TryGetValue(key, out var existing)) return existing;
        var feed = new CameraFeed(key, Dispatcher.CurrentDispatcher);
        Feeds[key] = feed;
        return feed;
    }

    /// <summary>Raised on the UI dispatcher for every captured frame.</summary>
    public event Action<BitmapSource>? FrameReady;

    /// <summary>The last capture error, shown in the dock instead of a silent black box.</summary>
    public string? Error { get; private set; }

    public bool IsLive => _wanted;

    /// <summary>Which capture device this feed opens.</summary>
    public int CameraIndex => _index;

    public bool CanRecord => !_recordingFailed && _segmentPath is not null;

    public string? LastReplayPath { get; private set; }

    public void Start()
    {
        if (_wanted) return;
        _wanted = true;
        Error = null;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = $"Camera {_index + 1}" };
        _thread.Start();
    }

    public void Stop()
    {
        _wanted = false;
        var thread = _thread;
        _thread = null;
        try { thread?.Join(1500); } catch { }
        lock (_gate) CloseWriter();
    }

    /// <summary>
    /// Closes the clip recorded so far and returns it for instant replay, then starts a fresh
    /// segment so the live feed keeps being recorded. Null when nothing could be recorded.
    /// </summary>
    public string? TakeReplay()
    {
        lock (_gate)
        {
            var path = _segmentPath;
            var length = DateTime.UtcNow - _segmentStarted;
            CloseWriter();
            if (path is null || !File.Exists(path) || length < TimeSpan.FromSeconds(1)) return null;
            LastReplayPath = path;
            return path;
        }
    }

    void CloseWriter()
    {
        try { _writer?.Release(); } catch { }
        try { _writer?.Dispose(); } catch { }
        _writer = null;
        _segmentPath = null;
    }

    void CaptureLoop()
    {
        VideoCapture? capture = null;
        try
        {
            capture = Open(_index);
            if (capture is null || !capture.IsOpened())
            {
                Error = $"Camera {_index + 1} is not available.";
                return;
            }
            using var frame = new Mat();
            var interval = (int)(1000 / FramesPerSecond);
            while (_wanted)
            {
                var started = Environment.TickCount64;
                if (!capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(60);
                    continue;
                }
                RecordFrame(frame);
                PublishFrame(frame);
                var spent = (int)(Environment.TickCount64 - started);
                var wait = interval - spent;
                if (wait > 0) Thread.Sleep(wait);
            }
        }
        catch (Exception ex)
        {
            Error = $"Camera {_index + 1}: {ex.Message}";
        }
        finally
        {
            lock (_gate) CloseWriter();
            try { capture?.Release(); capture?.Dispose(); } catch { }
            _wanted = false;
        }
    }

    static VideoCapture? Open(int index)
    {
        // DirectShow starts in well under a second, where Media Foundation can take several, and
        // its 15 fps at the camera's native size matches the recorder. Media Foundation is the
        // fallback for drivers that refuse DirectShow.
        try
        {
            var directShow = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (directShow.IsOpened()) return directShow;
            directShow.Dispose();
        }
        catch { }
        try { return new VideoCapture(index); } catch { return null; }
    }

    void RecordFrame(Mat frame)
    {
        if (_recordingFailed) return;
        lock (_gate)
        {
            if (_writer is null)
            {
                try
                {
                    var folder = AppPaths.Combine("CameraClips");
                    Directory.CreateDirectory(folder);
                    Prune(folder);
                    var path = Path.Combine(folder, $"camera-{DateTime.Now:yyyyMMdd-HHmmss}.avi");
                    var writer = new VideoWriter(path, FourCC.MJPG, FramesPerSecond,
                        new Size(Math.Max(2, frame.Width), Math.Max(2, frame.Height)));
                    if (!writer.IsOpened())
                    {
                        writer.Dispose();
                        _recordingFailed = true;
                        return;
                    }
                    _writer = writer;
                    _segmentPath = path;
                    _segmentStarted = DateTime.UtcNow;
                }
                catch
                {
                    _recordingFailed = true;
                    return;
                }
            }
            try
            {
                _writer!.Write(frame);
                // Long shows would otherwise fill the disk with one endless file.
                if (DateTime.UtcNow - _segmentStarted > SegmentLength) CloseWriter();
            }
            catch
            {
                CloseWriter();
                _recordingFailed = true;
            }
        }
    }

    /// <summary>Keeps the three newest camera clips so replays never grow without limit.</summary>
    static void Prune(string folder)
    {
        try
        {
            foreach (var stale in new DirectoryInfo(folder).GetFiles("camera-*.avi")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(3))
                stale.Delete();
        }
        catch { }
    }

    void PublishFrame(Mat frame)
    {
        if (_framePending) return;
        BitmapSource? bitmap = null;
        try
        {
            // Copy the raw BGR frame straight into a bitmap. Encoding every frame as JPEG and
            // decoding it again was the bottleneck: it capped the preview at about 12 fps.
            var stride = (int)frame.Step();
            var image = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null,
                frame.Data, stride * frame.Height, stride);
            image.Freeze();
            bitmap = image;
        }
        catch { return; }
        var picture = bitmap;
        _framePending = true;
        _dispatcher.BeginInvoke(() =>
        {
            _framePending = false;
            try { FrameReady?.Invoke(picture); } catch { }
        });
    }

    public void Dispose()
    {
        Stop();
        Feeds.Remove(_index);
    }
}
