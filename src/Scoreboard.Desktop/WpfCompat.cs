// ScorePlus Linux port — dirty WPF compatibility shims.
//
// TECH DEBT: these exist only so the ported code compiles while the real Avalonia/ffmpeg/pulse
// replacements land. Media/audio/camera paths remain compatibility stubs; animation calls now use
// a real Avalonia dispatcher clock so transitions and completion callbacks are time-based. Grep
// "WpfCompat" to find every shim site.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace Scoreboard.Desktop;

// ---------- WPF enums / small value types ----------

public enum Visibility { Visible, Hidden, Collapsed }

public enum MediaState { Manual, Pause, Stop, Close }

public enum PixelFormats { Pbgra32, Bgra32, Bgr24 }

public sealed class BitmapCacheOption
{
    public static readonly BitmapCacheOption OnLoad = new();
    public static readonly BitmapCacheOption None = new();
}

// ---------- images / thumbnails ----------

public class BitmapSource : Avalonia.Media.IImage
{
    protected Avalonia.Media.IImage? Image { get; set; }
    protected byte[]? Pixels { get; set; }
    public int PixelWidth { get; set; } = 1;
    public int PixelHeight { get; set; } = 1;
    public Avalonia.Size Size => new(PixelWidth, PixelHeight);

    public static BitmapSource Create(int width, int height, double dpiX, double dpiY,
        PixelFormats format, byte[]? palette, IntPtr pixels, int count, int stride)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(width, height),
            new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        var source = new byte[Math.Max(0, count)];
        if (pixels != IntPtr.Zero && count > 0) System.Runtime.InteropServices.Marshal.Copy(pixels, source, 0, count);
        var target = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            var outRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var from = row + x * (format == PixelFormats.Bgr24 ? 3 : 4);
                var to = outRow + x * 4;
                if (from + 2 >= source.Length) continue;
                target[to] = source[from];
                target[to + 1] = source[from + 1];
                target[to + 2] = source[from + 2];
                target[to + 3] = format == PixelFormats.Bgr24 ? (byte)255 : source.Length > from + 3 ? source[from + 3] : (byte)255;
            }
        }
        using (var locked = bitmap.Lock())
            System.Runtime.InteropServices.Marshal.Copy(target, 0, locked.Address, target.Length);
        return new BitmapSource { PixelWidth = width, PixelHeight = height, Image = bitmap, Pixels = target };
    }

    public void CopyPixels(byte[] buffer, int stride, int offset)
    {
        if (Pixels is null) return;
        var rowBytes = Math.Min(stride, Pixels.Length / Math.Max(1, PixelHeight));
        System.Buffer.BlockCopy(Pixels, 0, buffer, offset, Math.Min(Pixels.Length, buffer.Length - offset));
    }

    public void Freeze() { }
    public bool IsFrozen => true;

    void Avalonia.Media.IImage.Draw(Avalonia.Media.DrawingContext context,
        Avalonia.Rect sourceRect, Avalonia.Rect destRect) => Image?.Draw(context, sourceRect, destRect);
}

public sealed class BitmapImage : BitmapSource
{
    Avalonia.Media.Imaging.Bitmap? _decoded;
    public BitmapCacheOption CacheOption { get; set; } = BitmapCacheOption.OnLoad;
    public Uri? UriSource
    {
        get => null;
        set { if (value is not null) Load(value.IsFile ? value.LocalPath : value.ToString()); }
    }
    public System.IO.Stream? StreamSource
    {
        get => null;
        set { if (value is not null) { try { _decoded = new Avalonia.Media.Imaging.Bitmap(value); PixelWidth = _decoded.PixelSize.Width; PixelHeight = _decoded.PixelSize.Height; Image = _decoded; } catch { } } }
    }
    void Load(string path) { try { using var stream = System.IO.File.OpenRead(path); _decoded = new Avalonia.Media.Imaging.Bitmap(stream); PixelWidth = _decoded.PixelSize.Width; PixelHeight = _decoded.PixelSize.Height; Image = _decoded; } catch { } }
    public void BeginInit() { }
    public void EndInit() { }
}

public sealed class TransformedBitmap : BitmapSource
{
    public TransformedBitmap(Avalonia.Media.IImage source, Avalonia.Media.ITransform transform)
    {
        PixelWidth = Math.Max(1, (int)Math.Round(source.Size.Width));
        PixelHeight = Math.Max(1, (int)Math.Round(source.Size.Height));
    }
}

public sealed class BitmapFrame
{
    public static BitmapFrame Create(Avalonia.Media.IImage source) => new();
}

public sealed class JpegBitmapEncoder
{
    public int QualityLevel { get; set; } = 75;
    public List<BitmapFrame> Frames { get; } = [];
    public void Save(System.IO.Stream stream) { }
}

public sealed class RenderTargetBitmap : BitmapSource
{
    public RenderTargetBitmap(int width, int height, double dpiX, double dpiY, PixelFormats format)
    {
        PixelWidth = Math.Max(1, width);
        PixelHeight = Math.Max(1, height);
    }
    public void Render(object visual) { }
}

// Cropping preserves the requested placeholder dimensions, but does not create pixel data.
public sealed class CroppedBitmap : BitmapSource
{
    public CroppedBitmap(Avalonia.Media.IImage source)
    {
        PixelWidth = Math.Max(1, (int)Math.Round(source.Size.Width));
        PixelHeight = Math.Max(1, (int)Math.Round(source.Size.Height));
    }

    public CroppedBitmap(Avalonia.Media.IImage source, Int32Rect sourceRect)
    {
        PixelWidth = Math.Max(1, sourceRect.Width);
        PixelHeight = Math.Max(1, sourceRect.Height);
    }
}

public sealed class DrawingVisual
{
    public DrawingContext RenderOpen() => new();
}

public sealed class DrawingContext : IDisposable
{
    public void DrawVideo(MediaPlayer player, Avalonia.Rect rect) { } // stub: no frame source on Linux yet
    public void DrawRectangle(Brush? fill, Pen? pen, Avalonia.Rect rect) { }
    public void Dispose() { }
}

// ---------- media: MediaPlayer / MediaElement (real playback lands with the ffmpeg player) ----------

public sealed class MediaFailedEventArgsStub : EventArgs
{
    public Exception? ErrorException { get; init; }
}

public struct DurationLike
{
    public bool HasTimeSpan { get; }
    public TimeSpan TimeSpan { get; }
    public DurationLike(TimeSpan value) { HasTimeSpan = true; TimeSpan = value; }
    public static readonly DurationLike Automatic = default;
}

/// <summary>The transport player: a real clock and real audio, no picture (surfaces draw that).</summary>
public class MediaPlayer : IDisposable
{
    readonly FfmpegAudioPlayer _audio = new();
    readonly Avalonia.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly System.Diagnostics.Stopwatch _clock = new();
    bool _playing;
    double _base;
    double _duration;
    string? _path;
    bool _ended;

    public MediaPlayer()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public bool ScrubbingEnabled { get; set; }
    public double Volume { get => _audioVolume; set { _audioVolume = Math.Clamp(value, 0, 1); _audio.SetVolume(_audioVolume); } }
    double _audioVolume = 1;

    public TimeSpan Position
    {
        get => TimeSpan.FromSeconds(_playing ? Math.Min(_duration > 0 ? _duration : double.MaxValue, _base + _clock.Elapsed.TotalSeconds) : _base);
        set
        {
            _base = Math.Max(0, value.TotalSeconds);
            _clock.Restart();
            _ended = false;
            if (_path is not null && _audio.IsPlaying) _audio.Seek(_base);
        }
    }

    public DurationLike NaturalDuration { get; private set; }
    public Uri? Source { get; private set; }

    public event EventHandler MediaOpened = delegate { };
    public event EventHandler MediaEnded = delegate { };
    public event EventHandler<MediaFailedEventArgsStub> MediaFailed = delegate { };

    public void Open(Uri uri)
    {
        Close();
        Source = uri;
        _path = uri.IsFile ? uri.LocalPath : uri.ToString();
        var info = MediaFiles.Probe(_path);
        _duration = info.Duration;
        NaturalDuration = new DurationLike(TimeSpan.FromSeconds(_duration));
        _base = 0;
        _clock.Restart();
        _ended = false;
        if (!info.HasVideo && !info.HasAudio)
        {
            var failure = new MediaFailedEventArgsStub { ErrorException = new InvalidOperationException("the file has no readable audio or video") };
            Avalonia.Threading.Dispatcher.UIThread.Post(() => MediaFailed(this, failure));
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => MediaOpened(this, EventArgs.Empty));
    }

    public void Close()
    {
        _audio.Stop();
        _playing = false;
        _base = 0;
        _duration = 0;
        _path = null;
        Source = null;
        NaturalDuration = default;
    }

    public void Play()
    {
        if (_path is null) return;
        if (_ended)
        {
            _base = 0;
            _ended = false;
        }
        if (!_playing) _clock.Restart();
        _playing = true;
        var at = Position.TotalSeconds;
        if (_audioVolume > 0.001) _audio.Play(_path, null, _audioVolume, at);
    }

    public void Pause()
    {
        _base = Position.TotalSeconds;
        _clock.Stop();
        _playing = false;
        _audio.Pause();
    }

    public void Stop()
    {
        _base = 0;
        _clock.Reset();
        _playing = false;
        _ended = false;
        _audio.Stop();
    }

    void Tick()
    {
        if (!_playing || _duration <= 0) return;
        if (Position.TotalSeconds < _duration - 0.05) return;
        _base = _duration;
        _playing = false;
        _audio.Stop();
        if (_ended) return;
        _ended = true;
        MediaEnded(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _timer.Stop();
        Close();
        _audio.Dispose();
    }
}

/// <summary>
/// The picture inside a scorebug video box. It is a real Avalonia Image fed by an ffmpeg decoder;
/// the public surface keeps the WPF-shaped members the ported cue/seek/loop code already speaks.
/// </summary>
public sealed class MediaElement : Avalonia.Controls.Image, IDisposable
{
    FfmpegVideoDecoder? _decoder;
    FfmpegAudioPlayer? _audio;
    string? _path;
    double _duration;
    double _position;
    bool _playing;
    bool _muted;
    double _volume = 1;

    public MediaState LoadedBehavior { get; set; }
    public MediaState UnloadedBehavior { get; set; }
    public new bool IsMuted
    {
        get => _muted;
        set { _muted = value; ApplyAudio(); }
    }
    public new double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); _audio?.SetVolume(_volume); }
    }
    public bool ScrubbingEnabled { get; set; }
    public new Uri? Source
    {
        get => _path is null ? null : new Uri(_path);
        set => OpenSource(value);
    }
    public new TimeSpan Position
    {
        get => TimeSpan.FromSeconds(_position);
        set => Seek(value.TotalSeconds);
    }
    public DurationLike NaturalDuration => new(TimeSpan.FromSeconds(_duration));
    /// <summary>The bitmap currently drawn, exposed for the media checks and diagnostics.</summary>
    public Avalonia.Media.IImage? CurrentFrame => base.Source;

    public event EventHandler MediaOpened = delegate { };
    public event EventHandler MediaEnded = delegate { };
    public event EventHandler<MediaFailedEventArgsStub> MediaFailed = delegate { };

    public MediaElement()
    {
        Stretch = Stretch.Uniform;
    }

    void OpenSource(Uri? uri)
    {
        DisposePlayback();
        if (uri is null) { base.Source = null; return; }
        _path = uri.IsFile ? uri.LocalPath : uri.ToString();
        var info = MediaFiles.Probe(_path);
        _duration = info.Duration;
        _position = 0;
        _decoder = new FfmpegVideoDecoder();
        _decoder.FrameReady += frame => base.Source = frame;
        _decoder.Ended += () => { _playing = false; _audio?.Stop(); MediaEnded(this, EventArgs.Empty); };
        _decoder.Failed += message =>
        {
            var failure = new MediaFailedEventArgsStub { ErrorException = new InvalidOperationException(message) };
            MediaFailed(this, failure);
        };
        _decoder.Open(_path, 0, info.Fps > 0 ? info.Fps : 30);
        _decoder.Pause();
        MediaOpened(this, EventArgs.Empty);
    }

    void ApplyAudio()
    {
        if (_path is null) return;
        if (_muted || _volume <= 0.001)
        {
            _audio?.Stop();
            return;
        }
        _audio ??= new FfmpegAudioPlayer();
        if (_playing) _audio.Play(_path, null, _volume, _position);
    }

    public void Play()
    {
        if (_path is null) return;
        if (_decoder is null || !_decoder.IsRunning) OpenSource(new Uri(_path));
        _decoder?.Resume();
        _playing = true;
        ApplyAudio();
    }

    public void Pause()
    {
        _position = _decoder?.Position ?? _position;
        _decoder?.Pause();
        _playing = false;
        _audio?.Pause();
    }

    public void Stop()
    {
        _position = 0;
        _decoder?.Stop();
        _playing = false;
        _audio?.Stop();
        base.Source = null;
    }

    public void Close()
    {
        DisposePlayback();
        base.Source = null;
    }

    void Seek(double seconds)
    {
        if (_path is null) { _position = Math.Max(0, seconds); return; }
        _position = Math.Max(0, seconds);
        if (_decoder is null) return;
        _decoder.Seek(_path, _position);
        if (!_playing) _decoder.Pause();
        if (_audio is not null) _audio.Seek(_position);
    }

    void DisposePlayback()
    {
        _decoder?.Dispose();
        _decoder = null;
        _audio?.Dispose();
        _audio = null;
        _playing = false;
        _path = null;
        _duration = 0;
        _position = 0;
    }

    public void Dispose() => DisposePlayback();
}

internal static class MediaShimExt
{
    public static void MediaFailedHandlerHack() { }
}

// ---------- WPF-flavoured animation surface (snaps to end value, fires Completed immediately) ----------

public enum FillBehavior { HoldEnd, Stop }
public enum EasingMode { EaseIn, EaseOut, EaseInOut }

public interface IEasingFunction { }

public sealed class QuadraticEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
public sealed class CubicEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
public sealed class QuarticEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
public sealed class SineEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
public sealed class BackEase : IEasingFunction { public EasingMode EasingMode { get; set; } public double Amplitude { get; set; } }
public sealed class BounceEase : IEasingFunction { public EasingMode EasingMode { get; set; } public int Bounces { get; set; } public double Bounciness { get; set; } }
public sealed class ElasticEase : IEasingFunction { public EasingMode EasingMode { get; set; } public int Oscillations { get; set; } public double Springiness { get; set; } }

public struct RepeatBehavior
{
    public double CycleCount { get; }
    public RepeatBehavior(double cycles) { CycleCount = cycles; }
    public static readonly RepeatBehavior Forever = new(double.PositiveInfinity);
}

public struct KeyTime
{
    public TimeSpan TimeSpan { get; }
    public double Fraction { get; }
    public bool IsPercent { get; }
    KeyTime(TimeSpan span, double fraction, bool isPercent) { TimeSpan = span; Fraction = fraction; IsPercent = isPercent; }
    public static KeyTime FromTimeSpan(TimeSpan value) => new(value, 0, false);
    public static KeyTime FromPercent(double percent) => new(TimeSpan.Zero, percent, true);
}

public abstract class WpfAnimation
{
    public FillBehavior FillBehavior { get; set; } = FillBehavior.HoldEnd;
    public RepeatBehavior RepeatBehavior { get; set; } = new(1);
    public bool AutoReverse { get; set; }
    public event EventHandler? Completed;
    internal void RaiseCompleted() => Completed?.Invoke(this, EventArgs.Empty);
    internal abstract bool TryFinal(out object value);
    internal abstract object ValueAt(double progress);
    public abstract TimeSpan Duration { get; set; }
}

public sealed class DoubleAnimation : WpfAnimation
{
    public double From { get; }
    public double To { get; }
    public override TimeSpan Duration { get; set; }
    public IEasingFunction? EasingFunction { get; set; }
    public DoubleAnimation(double from, double to, TimeSpan duration)
    { From = from; To = to; Duration = duration; }
    public DoubleAnimation(object fromValue, object toValue, TimeSpan duration)
    { From = Convert.ToDouble(fromValue); To = Convert.ToDouble(toValue); Duration = duration; }
    internal override bool TryFinal(out object value) { value = To; return true; }
    internal override object ValueAt(double progress)
    {
        var eased = WpfAnimationMath.Ease(progress, EasingFunction);
        return From + (To - From) * eased;
    }
}

public sealed class LinearDoubleKeyFrame
{
    public double Value { get; }
    public KeyTime KeyTime { get; }
    public LinearDoubleKeyFrame(double value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public LinearDoubleKeyFrame(double value, KeyTime keyTime, IEasingFunction ease) { Value = value; KeyTime = keyTime; }
}

public sealed class EasingDoubleKeyFrame
{
    public double Value { get; }
    public KeyTime KeyTime { get; }
    public IEasingFunction? EasingFunction { get; set; }
    public EasingDoubleKeyFrame(double value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public EasingDoubleKeyFrame(double value, KeyTime keyTime, IEasingFunction ease) { Value = value; KeyTime = keyTime; EasingFunction = ease; }
}

public sealed class LinearColorKeyFrame
{
    public Color Value { get; }
    public KeyTime KeyTime { get; }
    public LinearColorKeyFrame(Color value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
}

public sealed class EasingColorKeyFrame
{
    public Color Value { get; }
    public KeyTime KeyTime { get; }
    public IEasingFunction? EasingFunction { get; set; }
    public EasingColorKeyFrame(Color value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
}

public sealed class DoubleAnimationUsingKeyFrames : WpfAnimation
{
    public List<object> KeyFrames { get; } = [];
    public override TimeSpan Duration { get; set; }
    internal override bool TryFinal(out object value) { value = ValueAt(1); return true; }
    internal override object ValueAt(double progress) => WpfAnimationMath.DoubleAt(KeyFrames, Duration, progress);
}

public sealed class ColorAnimationUsingKeyFrames : WpfAnimation
{
    public List<object> KeyFrames { get; } = [];
    public override TimeSpan Duration { get; set; }
    internal override bool TryFinal(out object value) { value = ValueAt(1); return true; }
    internal override object ValueAt(double progress) => WpfAnimationMath.ColorAt(KeyFrames, Duration, progress);
}

internal static class WpfAnimationMath
{
    public static double Ease(double value, IEasingFunction? function)
    {
        value = Math.Clamp(value, 0, 1);
        if (function is null) return value;
        double In(double p) => function switch
        {
            QuadraticEase => p * p,
            CubicEase => p * p * p,
            QuarticEase => p * p * p * p,
            SineEase => 1 - Math.Cos(p * Math.PI / 2),
            BackEase back => p * p * ((back.Amplitude + 1) * p - back.Amplitude),
            _ => p
        };
        var mode = function switch
        {
            QuadraticEase q => q.EasingMode, CubicEase c => c.EasingMode, QuarticEase q => q.EasingMode,
            SineEase s => s.EasingMode, BackEase b => b.EasingMode, BounceEase b => b.EasingMode,
            ElasticEase e => e.EasingMode, _ => EasingMode.EaseInOut
        };
        return mode switch
        {
            EasingMode.EaseIn => In(value), EasingMode.EaseOut => 1 - In(1 - value),
            _ => value < 0.5 ? In(value * 2) / 2 : 1 - In((1 - value) * 2) / 2
        };
    }

    static double Time(KeyTime key, TimeSpan duration) => key.IsPercent ? key.Fraction : duration.TotalSeconds <= 0 ? 1 : key.TimeSpan.TotalSeconds / duration.TotalSeconds;

    public static double DoubleAt(List<object> frames, TimeSpan duration, double progress)
    {
        var values = frames.Select(frame => frame switch
        {
            LinearDoubleKeyFrame linear => (linear.Value, linear.KeyTime, (IEasingFunction?)null),
            EasingDoubleKeyFrame easing => (easing.Value, easing.KeyTime, easing.EasingFunction),
            _ => (1d, KeyTime.FromPercent(1), (IEasingFunction?)null)
        }).ToList();
        if (values.Count == 0) return 1;
        progress = Math.Clamp(progress, 0, 1);
        var at = values.Select(value => (value.Item1, Time: Time(value.Item2, duration), value.Item3)).ToList();
        if (progress <= at[0].Time) return at[0].Item1;
        for (var i = 1; i < at.Count; i++)
        {
            if (progress > at[i].Time) continue;
            var span = Math.Max(0.000001, at[i].Time - at[i - 1].Time);
            var local = Ease((progress - at[i - 1].Time) / span, at[i].Item3);
            return at[i - 1].Item1 + (at[i].Item1 - at[i - 1].Item1) * local;
        }
        return at[^1].Item1;
    }

    public static Color ColorAt(List<object> frames, TimeSpan duration, double progress)
    {
        var values = frames.Select(frame => frame switch
        {
            LinearColorKeyFrame linear => (linear.Value, linear.KeyTime, (IEasingFunction?)null),
            EasingColorKeyFrame easing => (easing.Value, easing.KeyTime, easing.EasingFunction),
            _ => (Colors.White, KeyTime.FromPercent(1), (IEasingFunction?)null)
        }).ToList();
        if (values.Count == 0) return Colors.White;
        progress = Math.Clamp(progress, 0, 1);
        var at = values.Select(value => (value.Item1, Time: Time(value.Item2, duration), value.Item3)).ToList();
        if (progress <= at[0].Time) return at[0].Item1;
        for (var i = 1; i < at.Count; i++)
        {
            if (progress > at[i].Time) continue;
            var span = Math.Max(0.000001, at[i].Time - at[i - 1].Time);
            var local = Ease((progress - at[i - 1].Time) / span, at[i].Item3);
            var a = at[i - 1].Item1; var b = at[i].Item1;
            return Color.FromArgb((byte)Math.Round(a.A + (b.A - a.A) * local), (byte)Math.Round(a.R + (b.R - a.R) * local), (byte)Math.Round(a.G + (b.G - a.G) * local), (byte)Math.Round(a.B + (b.B - a.B) * local));
        }
        return at[^1].Item1;
    }
}

/// <summary>Runs the existing WPF-shaped animation calls with a small Avalonia dispatcher clock.</summary>
public static class WpfAnimationExtensions
{
    sealed class Running
    {
        public required AvaloniaObject Target;
        public required AvaloniaProperty Property;
        public required WpfAnimation Animation;
        public long Started;
    }

    static readonly List<Running> Active = [];
    static DispatcherTimer? _timer;

    public static void BeginAnimation(this AvaloniaObject target, AvaloniaProperty property, WpfAnimation? animation)
    {
        Active.RemoveAll(run => ReferenceEquals(run.Target, target) && run.Property == property);
        if (animation is null) { StopClockIfIdle(); return; }
        var run = new Running { Target = target, Property = property, Animation = animation, Started = Stopwatch.GetTimestamp() };
        Active.Add(run);
        Apply(run, 0);
        EnsureClock();
    }

    static void EnsureClock()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    static void StopClockIfIdle()
    {
        if (Active.Count != 0) return;
        _timer?.Stop();
        _timer = null;
    }

    static void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        for (var i = Active.Count - 1; i >= 0; i--)
        {
            var run = Active[i];
            var duration = Math.Max(0.001, run.Animation.Duration.TotalSeconds);
            var elapsed = (now - run.Started) / (double)Stopwatch.Frequency;
            var cycleLength = duration * (run.Animation.AutoReverse ? 2 : 1);
            var finite = !double.IsPositiveInfinity(run.Animation.RepeatBehavior.CycleCount);
            var total = cycleLength * Math.Max(1, run.Animation.RepeatBehavior.CycleCount);
            if (finite && elapsed >= total)
            {
                Apply(run, run.Animation.AutoReverse && ((long)Math.Ceiling(run.Animation.RepeatBehavior.CycleCount) & 1) != 0 ? 0 : 1);
                Active.RemoveAt(i);
                run.Animation.RaiseCompleted();
                continue;
            }
            var within = elapsed % cycleLength;
            var progress = within / duration;
            Apply(run, progress > 1 ? 2 - progress : progress);
        }
        StopClockIfIdle();
    }

    static void Apply(Running run, double progress)
    {
        try { run.Target.SetValue(run.Property, run.Animation.ValueAt(progress)); } catch { }
    }
}


/// <summary>WPF's Slider value-change args; Avalonia handlers adapt to these at the wire sites.</summary>
public sealed class RoutedPropertyChangedEventArgs<T> : EventArgs
{
    public T OldValue { get; init; } = default!;
    public T NewValue { get; init; } = default!;
}

// ---------- NAudio stand-ins (WASAPI does not exist on Linux; real path = pw-play/ffmpeg) ----------
// TECH DEBT: every method below is inert. Audio routing, mic monitor, and the output meter report
// "available" but produce no sound. TODO(port): replace with PipeWire/ffmpeg players.

public enum DataFlow { Render, Capture }
public enum DeviceState { Active = 1, Uninitialized = 2, Disabled = 4, NotPresent = 8 }
public enum Role { Console, Multimedia, Communications }
public enum AudioClientShareMode { Shared, Exclusive }
public enum WaveFormatEncoding { Pcm = 1, IeeeFloat = 3, Extensible = 0xFFFE }

public sealed class WaveFormat
{
    public int BitsPerSample { get; } = 32;
    public WaveFormatEncoding Encoding { get; } = WaveFormatEncoding.IeeeFloat;
    public int SampleRate { get; } = 48000;
    public int Channels { get; } = 2;
}

public sealed class WaveInEventArgs : EventArgs
{
    public byte[] Buffer { get; init; } = [];
    public int BytesRecorded { get; init; }
}

public interface IWavePlayer
{
    void Init(object audioSource);
    void Play();
    void Pause();
    void Stop();
    void Dispose();
}

public sealed class MMDevice : IDisposable
{
    public string ID { get; init; } = "stub-device";
    public string FriendlyName { get; init; } = "Stub output";
    public void Dispose() { }
}

public sealed class MMDeviceEnumerator : IDisposable
{
    public List<MMDevice> EnumerateAudioEndPoints(DataFlow flow, DeviceState states) => [];
    public MMDevice? GetDefaultAudioEndpoint(DataFlow flow, Role role) => null; // stub: no devices
    public MMDevice? GetDevice(string id) => null;
    public void Dispose() { }
}

public sealed class AudioFileReader : IDisposable
{
    public float Volume { get; set; } = 1;
    public TimeSpan TotalTime => TimeSpan.Zero;
    public TimeSpan CurrentTime { get; set; }
    public AudioFileReader(string path) { }
    public void Dispose() { }
}

public sealed class BufferedWaveProvider
{
    public bool DiscardOnBufferOverflow { get; set; }
    public bool ReadFully { get; set; }
    public BufferedWaveProvider(WaveFormat format) { }
    public void AddSamples(byte[] buffer, int offset, int count) { }
}

public abstract class WasapiDeviceStub : IDisposable
{
    public WaveFormat WaveFormat { get; } = new();
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public void StartRecording() { }
    public void StopRecording() { }
    public void Dispose() { }
    protected void RaiseData(WaveInEventArgs e) => DataAvailable?.Invoke(this, e);
}

public sealed class WasapiCapture : WasapiDeviceStub
{
    public WasapiCapture() { }
    public WasapiCapture(MMDevice device) { }
}

public sealed class WasapiLoopbackCapture : WasapiDeviceStub
{
    public WasapiLoopbackCapture() { }
}

public sealed class WasapiOut : IWavePlayer
{
    public WasapiOut(AudioClientShareMode shareMode, bool useEventSync, int latency) { }
    public WasapiOut(MMDevice device, AudioClientShareMode shareMode, bool useEventSync, int latency) { }
    public void Init(object audioSource) { }
    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void Dispose() { }
}

// ---------- OpenCV stand-ins (OpenCvSharp4 was dropped; real path = ffmpeg/v4l2) ----------
// TECH DEBT: cameras never open (IsOpened false) so live feed and replay stay unavailable.
// TODO(port): ffmpeg-based LinuxCameraFeed.

public enum VideoCaptureAPIs { ANY, V4L2, DSHOW }
public enum VideoCaptureProperties { PosFrames = 1, FrameWidth = 3, FrameHeight = 4 }

public sealed class FourCC
{
    public static readonly FourCC MJPG = new();
}

/// <summary>One raw BGR frame. ffmpeg writes straight into this buffer, so no codec SDK is needed.</summary>
public sealed class Mat : IDisposable
{
    byte[] _pixels = [];
    System.Runtime.InteropServices.GCHandle _handle;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public long Step() => Width * 3L;
    public IntPtr Data => _handle.IsAllocated ? _handle.AddrOfPinnedObject() : IntPtr.Zero;
    public bool Empty() => Width <= 0 || Height <= 0 || _pixels.Length == 0;

    internal void SetFrame(byte[] pixels, int width, int height)
    {
        if (_handle.IsAllocated) _handle.Free();
        _pixels = pixels;
        Width = width;
        Height = height;
        _handle = System.Runtime.InteropServices.GCHandle.Alloc(_pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
    }

    public void Dispose()
    {
        if (_handle.IsAllocated) _handle.Free();
        _pixels = [];
        Width = Height = 0;
    }
}

/// <summary>ffmpeg-backed camera/file capture, replacing OpenCV's VideoCapture.</summary>
public sealed class VideoCapture : IDisposable
{
    readonly object _gate = new();
    Process? _process;
    System.IO.Stream? _output;
    string? _path;
    int _width, _height;
    double _fps = 30, _duration;
    long _frameCount;
    bool _camera;
    int _index;
    long _readFrames;

    public VideoCapture(string path)
    {
        _path = path;
        var info = MediaFiles.Probe(path);
        _width = Math.Max(2, info.Width);
        _height = Math.Max(2, info.Height);
        _fps = info.Fps > 1 && info.Fps < 121 ? info.Fps : 30;
        _duration = info.Duration;
        _frameCount = (long)Math.Round(_duration * _fps);
        Start(0);
    }

    public VideoCapture(int index) => OpenCamera(index);
    public VideoCapture(int index, VideoCaptureAPIs api) => OpenCamera(index);

    void OpenCamera(int index)
    {
        _camera = true;
        _index = Math.Max(0, index);
        _width = 1280;
        _height = 720;
        _fps = 15;
        Start(0);
    }

    void Start(double seconds)
    {
        lock (_gate)
        {
            StopProcess();
            if (MediaProbe.Ffmpeg is null) return;
            var args = new List<string> { "-v", "error" };
            if (seconds > 0.05)
            {
                args.Add("-ss");
                args.Add(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (_camera)
            {
                args.AddRange(["-f", "v4l2", "-framerate", "15", "-i", $"/dev/video{_index}",
                    "-vf", "scale=1280:720", "-an", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]);
            }
            else
            {
                args.AddRange(["-i", _path!, "-an", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]);
                _frameCount = (long)Math.Max(0, _frameCount);
            }
            try
            {
                _process = Process.Start(MediaProbe.Start(MediaProbe.Ffmpeg, args.ToArray()));
                _output = _process?.StandardOutput.BaseStream;
                _readFrames = (long)Math.Round(seconds * _fps);
            }
            catch { _process = null; _output = null; }
        }
    }

    public bool IsOpened() => _process is { HasExited: false };
    public double Fps => _fps;
    public long FrameCount => _frameCount;

    public void Set(VideoCaptureProperties property, double value)
    {
        if (property != VideoCaptureProperties.PosFrames || _camera || _path is null) return;
        var frame = Math.Max(0, value);
        Start(frame / Math.Max(1, _fps));
    }

    public bool Read(Mat frame)
    {
        lock (_gate)
        {
            if (_output is null || _width <= 0 || _height <= 0) return false;
            var size = _width * _height * 3;
            var buffer = new byte[size];
            var read = 0;
            try
            {
                while (read < size)
                {
                    var count = _output.Read(buffer, read, size - read);
                    if (count <= 0) return false;
                    read += count;
                }
            }
            catch { return false; }
            _readFrames++;
            frame.SetFrame(buffer, _width, _height);
            return true;
        }
    }

    public void Release() { lock (_gate) StopProcess(); }

    void StopProcess()
    {
        try { _output?.Dispose(); } catch { }
        _output = null;
        var process = _process;
        _process = null;
        try { if (process is { HasExited: false }) process.Kill(true); } catch { }
        try { process?.Dispose(); } catch { }
    }

    public void Dispose() => Release();
}

/// <summary>Real MJPEG AVI recording through ffmpeg, replacing OpenCV's VideoWriter.</summary>
public sealed class VideoWriter : IDisposable
{
    Process? _process;
    System.IO.Stream? _input;
    int _width, _height;

    public VideoWriter(string path, FourCC fourCC, double fps, Avalonia.Size frameSize)
    {
        if (MediaProbe.Ffmpeg is null) return;
        _width = Math.Max(2, (int)Math.Round(frameSize.Width));
        _height = Math.Max(2, (int)Math.Round(frameSize.Height));
        try
        {
            var info = MediaProbe.Start(MediaProbe.Ffmpeg,
                "-y", "-v", "error", "-f", "rawvideo", "-pix_fmt", "bgr24",
                "-s", $"{_width}x{_height}", "-r", fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                "-i", "-", "-an", "-c:v", "mjpeg", "-q:v", "5", path);
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = false;
            _process = Process.Start(info);
            _input = _process?.StandardInput.BaseStream;
        }
        catch { _process = null; _input = null; }
    }

    public bool IsOpened() => _process is { HasExited: false };

    public void Write(Mat frame)
    {
        if (_input is null || frame.Empty()) return;
        try
        {
            var bytes = new byte[frame.Step() * frame.Height];
            System.Runtime.InteropServices.Marshal.Copy(frame.Data, bytes, 0, bytes.Length);
            _input.Write(bytes, 0, bytes.Length);
        }
        catch { Release(); }
    }

    public void Release()
    {
        try { _input?.Close(); } catch { }
        _input = null;
        var process = _process;
        _process = null;
        try { if (process is { HasExited: false }) process.WaitForExit(1000); } catch { }
        try { process?.Dispose(); } catch { }
    }

    public void Dispose() => Release();
}
