using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Scoreboard.Desktop;

/// <summary>
/// Locates the Linux media tools once and reports a useful status when one is missing. Nothing here
/// starts a process just because the app opened.
/// </summary>
public static class MediaProbe
{
    public static string? Ffmpeg { get; } = Find("ffmpeg");
    public static string? Ffprobe { get; } = Find("ffprobe");
    public static string? Ffplay { get; } = Find("ffplay");
    public static string? PulseAudio { get; } = Find("pactl");
    public static string? Paplay { get; } = Find("paplay");
    public static string? Parec { get; } = Find("parec");
    public static string? Xrandr { get; } = Find("xrandr");
    public static string? Fonts { get; } = Find("fc-list");
    public static bool HasVideo => Ffmpeg is not null && Ffprobe is not null;
    public static bool HasAudio => Ffplay is not null || Paplay is not null;

    public static string VideoStatus => HasVideo
        ? "ffmpeg and ffprobe ready"
        : "ffmpeg/ffprobe not found — install ffmpeg to play video";

    public static string AudioStatus => HasAudio
        ? "audio output ready"
        : "ffplay/paplay not found — install ffmpeg or pulseaudio-utils for sound";

    static string? Find(string name)
    {
        try
        {
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path)) return path;
            }
        }
        catch { }
        return null;
    }

    public static ProcessStartInfo Start(string file, params string[] arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }
}

/// <summary>Video/audio stream facts read from ffprobe. Missing streams are zero/false.</summary>
public sealed record ProbeInfo(int Width, int Height, double Fps, double Duration, bool HasVideo, bool HasAudio)
{
    public static readonly ProbeInfo Empty = new(0, 0, 0, 0, false, false);
}

public static class MediaFiles
{
    static readonly Dictionary<string, ProbeInfo> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object Gate = new();

    /// <summary>Reads a clip's dimensions, rate and duration. A failed probe yields Empty.</summary>
    public static ProbeInfo Probe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || MediaProbe.Ffprobe is null)
            return ProbeInfo.Empty;
        lock (Gate) if (Cache.TryGetValue(path, out var cached)) return cached;
        try
        {
            using var process = Process.Start(MediaProbe.Start(MediaProbe.Ffprobe,
                "-v", "error", "-show_streams", "-show_format", "-of", "json", path));
            if (process is null) return ProbeInfo.Empty;
            var json = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var info = ProbeInfo.Empty;
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var kind = stream.TryGetProperty("codec_type", out var type) ? type.GetString() : null;
                    if (kind == "video" && !info.HasVideo)
                    {
                        var width = Number(stream, "width");
                        var height = Number(stream, "height");
                        var fps = Fraction(stream.TryGetProperty("avg_frame_rate", out var rate) ? rate.GetString() : null)
                            ?? Fraction(stream.TryGetProperty("r_frame_rate", out var rate2) ? rate2.GetString() : null)
                            ?? 30;
                        var duration = Number(stream, "duration", -1);
                        info = info with { Width = Math.Max(0, (int)width), Height = Math.Max(0, (int)height), Fps = fps, Duration = duration, HasVideo = true };
                    }
                    else if (kind == "audio") info = info with { HasAudio = true };
                }
            }
            if (info.Duration <= 0 && root.TryGetProperty("format", out var format))
                info = info with { Duration = Number(format, "duration", 0) };
            lock (Gate) Cache[path] = info;
            return info;
        }
        catch { return ProbeInfo.Empty; }
    }

    static double Number(JsonElement element, string name, double fallback = 0)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var direct)) return direct;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        return fallback;
    }

    static double? Fraction(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('/');
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var top)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var bottom) && bottom > 0)
            return top / bottom;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) ? single : null;
    }
}

/// <summary>
/// Decodes a video file through ffmpeg on a worker thread and turns each raw BGRA frame into a
/// persistent Avalonia bitmap. The latest frame wins: a slow UI drops old pictures instead of
/// queueing them, so playback stays in real time.
/// </summary>
public sealed class FfmpegVideoDecoder : IDisposable
{
    readonly object _gate = new();
    Process? _process;
    Thread? _thread;
    CancellationTokenSource? _cancel;
    WriteableBitmap? _bitmap;
    byte[]? _pixels;
    int _width;
    int _height;
    double _fps = 30;
    double _baseSeconds;
    long _frames;
    volatile bool _paused;
    volatile bool _disposed;
    bool _frameQueued;

    public event Action<WriteableBitmap>? FrameReady;
    public event Action? Ended;
    public event Action<string>? Failed;

    public double Duration { get; private set; }
    public double Position => _baseSeconds + _frames / Math.Max(1, _fps);
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Starts (or restarts) decoding at a point in the file.</summary>
    public void Open(string path, double startSeconds = 0, double fps = 30)
    {
        Stop();
        if (MediaProbe.Ffmpeg is null) { Failed?.Invoke("ffmpeg is not installed or not on PATH"); return; }
        var info = MediaFiles.Probe(path);
        if (info.Width <= 0 || info.Height <= 0)
        {
            // A split/stream may not report dimensions up front; let ffmpeg use a sane default.
            info = info with { Width = 1280, Height = 720 };
        }
        _width = Math.Max(2, info.Width);
        _height = Math.Max(2, info.Height);
        _fps = info.Fps > 1 && info.Fps < 121 ? info.Fps : Math.Max(1, fps);
        Duration = info.Duration;
        _baseSeconds = Math.Max(0, startSeconds);
        _frames = 0;
        _paused = false;
        _disposed = false;
        _bitmap = new WriteableBitmap(new Avalonia.PixelSize(_width, _height), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        _pixels = new byte[_width * _height * 4];
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        StartProcess(path, _baseSeconds);
        _thread = new Thread(() => ReadLoop(token)) { IsBackground = true, Name = "ffmpeg video" };
        _thread.Start();
    }

    void StartProcess(string path, double start)
    {
        var info = MediaProbe.Start(MediaProbe.Ffmpeg,
            "-v", "error",
            "-ss", Math.Max(0, start).ToString("0.###", CultureInfo.InvariantCulture),
            "-i", path,
            "-an",
            "-vf", $"scale={_width}:{_height}:force_original_aspect_ratio=decrease,pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2",
            "-r", _fps.ToString("0.###", CultureInfo.InvariantCulture),
            "-f", "rawvideo", "-pix_fmt", "bgra", "-");
        _process = Process.Start(info);
        if (_process is null) Failed?.Invoke("ffmpeg could not be started");
    }

    void ReadLoop(CancellationToken token)
    {
        var process = _process;
        if (process?.StandardOutput.BaseStream is not { } stream || _pixels is null) return;
        var frameSize = _width * _height * 4;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_paused) { Thread.Sleep(15); continue; }
                var read = 0;
                while (read < frameSize)
                {
                    var count = stream.Read(_pixels, read, frameSize - read);
                    if (count <= 0) break;
                    read += count;
                }
                if (read < frameSize) break;
                _frames++;
                var copy = (byte[])_pixels.Clone();
                QueueFrame(copy);
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Failed?.Invoke($"video decode: {ex.Message}");
        }
        finally
        {
            if (!_disposed) Dispatcher.BeginInvoke(() => Ended?.Invoke());
        }
    }

    void QueueFrame(byte[] copy)
    {
        lock (_gate)
        {
            if (_frameQueued) return;
            _frameQueued = true;
        }
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_disposed || _bitmap is null) return;
                using (var locked = _bitmap.Lock())
                {
                    System.Runtime.InteropServices.Marshal.Copy(copy, 0, locked.Address, Math.Min(copy.Length, _width * _height * 4));
                }
                FrameReady?.Invoke(_bitmap);
            }
            catch { }
            finally { lock (_gate) _frameQueued = false; }
        });
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public void Seek(string path, double seconds)
    {
        Stop();
        Open(path, seconds, _fps);
    }

    public void Stop()
    {
        var cancel = _cancel;
        _cancel = null;
        try { cancel?.Cancel(); } catch { }
        var process = _process;
        _process = null;
        try { if (process is { HasExited: false }) process.Kill(true); } catch { }
        try { process?.Dispose(); } catch { }
        var thread = _thread;
        _thread = null;
        if (thread is not null && thread != Thread.CurrentThread) { try { thread.Join(300); } catch { } }
        cancel?.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _bitmap = null;
        _pixels = null;
    }
}

/// <summary>
/// Plays one clip's sound through ffplay, optionally onto a named PulseAudio sink. This is used by
/// the horn, routed speaker clips and the audio-only transport, so no sound card is hard-wired.
/// </summary>
public sealed class FfmpegAudioPlayer : IDisposable
{
    Process? _process;
    readonly Stopwatch _clock = new();
    double _base;
    bool _playing;
    string? _path;
    string? _sink;
    double _volume = 1;
    double _duration;
    int _pid;

    public bool IsPlaying => _playing;
    public string? LastError { get; private set; }
    public double Position => Math.Min(_duration > 0 ? _duration : double.MaxValue, _base + _clock.Elapsed.TotalSeconds);
    public double Duration => _duration;

    public void Play(string path, string? sink, double volume, double startSeconds = 0)
    {
        Stop();
        LastError = null;
        _path = path;
        _sink = sink;
        _volume = Math.Clamp(volume, 0, 1);
        _duration = MediaFiles.Probe(path).Duration;
        var start = Math.Clamp(startSeconds, 0, _duration > 0 ? Math.Max(0, _duration - 0.05) : 86400);
        if (!StartProcess(path, start))
        {
            _path = null;
            return;
        }
        _base = start;
        _clock.Restart();
        _playing = true;
    }

    bool StartProcess(string path, double start)
    {
        if (!File.Exists(path)) { LastError = "the file is missing"; return false; }
        if (MediaProbe.Ffplay is not null)
        {
            var info = MediaProbe.Start(MediaProbe.Ffplay,
                "-nodisp", "-autoexit", "-loglevel", "error",
                "-ss", Math.Max(0, start).ToString("0.###", CultureInfo.InvariantCulture),
                "-volume", Math.Round(_volume * 100).ToString(CultureInfo.InvariantCulture),
                path);
            if (!string.IsNullOrWhiteSpace(_sink)) info.Environment["PULSE_SINK"] = _sink;
            try
            {
                _process = Process.Start(info);
                if (_process is null) { LastError = "ffplay could not be started"; return false; }
                _pid = _process.Id;
                _process.EnableRaisingEvents = true;
                _process.Exited += (_, _) => _playing = false;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }
        // paplay is a smaller dependency, but it takes WAV/FLAC, not compressed video audio.
        LastError = "ffplay is not installed or not on PATH";
        return false;
    }

    public void SetVolume(double volume)
    {
        var next = Math.Clamp(volume, 0, 1);
        if (Math.Abs(next - _volume) < 0.001) return;
        _volume = next;
        if (_path is not null && _playing)
        {
            var at = Position;
            Play(_path, _sink, _volume, at);
        }
    }

    public void Pause()
    {
        if (!_playing) return;
        Signal(19); // SIGSTOP
        _base = Position;
        _clock.Stop();
        _playing = false;
    }

    public void Resume()
    {
        if (_playing || _process is null || _process.HasExited) return;
        Signal(18); // SIGCONT
        _clock.Start();
        _playing = true;
    }

    public void Seek(double seconds)
    {
        if (_path is null) return;
        var wasPlaying = _playing;
        Play(_path, _sink, _volume, Math.Max(0, seconds));
        if (!wasPlaying) Pause();
    }

    void Signal(int number)
    {
        if (_pid <= 0 || !OperatingSystem.IsLinux()) return;
        try { kill(_pid, number); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    static extern int kill(int pid, int sig);

    public void Stop()
    {
        _playing = false;
        _clock.Stop();
        _clock.Reset();
        var process = _process;
        _process = null;
        _pid = 0;
        try { if (process is { HasExited: false }) process.Kill(true); } catch { }
        try { process?.Dispose(); } catch { }
    }

    public void Dispose() => Stop();
}
