using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Scoreboard.Core;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;

namespace Scoreboard.Desktop;

/// <summary>
/// The picture inside a scorebug video element. It watches <see cref="MediaPlayback"/> and only
/// shows the clip when the clip's tag matches the element's tag, so an "Ads" element stays empty
/// while a highlight reel is cued and vice versa. While cued it loops like a stadium screen, and it
/// freezes on the current frame whenever the operator pauses the transport.
/// </summary>
public sealed class VideoSurface : Grid
{
    ScorebugElement _item;
    readonly MediaPlayback? _playback;
    readonly MediaElement _element;
    readonly TextBlock? _placeholder = null;
    readonly RectangleGeometry _clip = new();
    // The picture layers move together for a crop: the surface stays the element's box, while the
    // larger picture behind it is shifted so only the chosen fraction of the source shows.
    readonly Grid _picture = new() { ClipToBounds = true };
    // Live camera and replay frames are drawn on these images instead of the MediaElement.
    readonly Image _live = new() { Stretch = Stretch.UniformToFill, IsVisible = false };
    readonly Image _replay = new() { Stretch = Stretch.UniformToFill, IsVisible = false };
    ReplayFeed? _replayFeed;
    BitmapSource? _liveFrame;
    BitmapSource? _replayFrame;
    // Identifies the cue currently on this surface, so a cut to another clip can snap the outgoing
    // picture and animate it away.
    string? _cueKey;
    Control? _switchOverlay;
    int _switchToken;
    readonly bool _mute;
    // True for a surface that is showing what the audience gets, even when it is muted. The
    // audience-view monitor is one: silent, but it must never show a replay the audience cannot.
    readonly bool _onAir;
    CameraFeed? _feed;
    int _cameraIndex = -1;
    string? _loadedPath;
    bool _playing;
    int _appliedSeek;
    // True once the decoder has opened the file, so a re-attached surface knows it already has
    // media loaded and should resume rather than treat itself as brand new.
    bool _opened;
    // A seek while playing pauses the picture and resumes it on the next dispatcher turn; the
    // queued resume owns Play() so this method does not start it inside the same tick.
    bool _resumeQueued;
    // Identifies the cue a queued freeze belongs to, so a timer left over from the previous clip
    // can never pause the new one.
    int _freezeToken;
    // Per-element audio. When this element names a speaker, its clip plays through that output
    // here, so two video boxes can carry two clips to two different speakers.
    AudioRouter? _router;
    string? _routedPath;
    string? _routedDevice;

    /// <param name="showPlaceholder">True only in the studio. The audience output and the preview
    /// stay blank while nothing is cued, so an off-air "VIDEO · GENERAL" label can never go out.</param>
    public VideoSurface(ScorebugElement item, MediaPlayback? playback, bool mute, bool showPlaceholder = true, bool onAir = false)
    {
        _item = item;
        _playback = playback;
        _mute = mute;
        _onAir = onAir;
        ClipToBounds = true;
        // MediaElement's video texture can paint past its layout slot when the aspect ratios differ,
        // so the picture is clipped to the element box explicitly instead of relying on layout only.
        Clip = _clip;
        SizeChanged += (_, _) => { SyncClip(); ApplyCrop(); };
        _element = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = item.PreserveVideoAspect ? Stretch.Uniform : Stretch.UniformToFill,
            IsMuted = mute,
            Volume = mute ? 0 : 1,
            // Needed so a seek while paused still draws the new frame on the scorebug.
            ScrubbingEnabled = true,
            ClipToBounds = true
        };
        // Re-apply once the MediaElement is in the tree so Play() is honoured. A surface that is
        // re-attached (the studio rebuilt its canvas) picks the picture up where the transport is
        // instead of clearing its source and restarting from frame one.
        _element.Loaded += (_, _) =>
        {
            if (_opened && _playback is { LivePosition: > 0.05 } transport)
            {
                try { _element.Position = TimeSpan.FromSeconds(transport.LivePosition); } catch { }
                _appliedSeek = _playback.SeekVersion;
                _playing = false;
            }
            Apply();
        };
        // A newly opened clip starts from the top; a paused cue holds its very first frame.
        _element.MediaOpened += (_, _) =>
        {
            _opened = true;
            // Open where the clip actually is: a rebuilt scorebug resumes the picture instead of
            // restarting it, and a fresh cue still starts from the operator's last seek.
            var resume = _playback?.LivePosition ?? 0;
            if (!double.IsFinite(resume) || resume <= 0.05) resume = _playback?.Position ?? 0;
            // Never ask for a point past the end of the clip: seeking beyond a short replay clip
            // left the box black until the transport moved the playhead again.
            if (_element.NaturalDuration.HasTimeSpan && _element.NaturalDuration.TimeSpan.TotalSeconds > 0)
                resume = Math.Min(resume, Math.Max(0, _element.NaturalDuration.TimeSpan.TotalSeconds - 0.1));
            try { _element.Position = TimeSpan.FromSeconds(Math.Max(0, resume)); } catch { }
            _appliedSeek = _playback?.SeekVersion ?? 0;
            if (_playback is { IsPlaying: false } && !_playing)
            {
                // Start decoding so a paused cue still shows its frame. Pausing on the very next
                // dispatcher turn froze some decoders before they had composited anything, which
                // is what left a black box in the preview until the clip was started. The freeze
                // now waits for the first frames to land.
                var token = ++_freezeToken;
                try { _element.Play(); } catch { }
                var freeze = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
                freeze.Tick += (_, _) =>
                {
                    freeze.Stop();
                    if (token != _freezeToken) return;
                    if (_live.Source is not null) _live.IsVisible = false;
                    // Start replay may have been pressed while the decoder was opening; in that
                    // case keep rolling instead of freezing on the first frame.
                    if (_playback is { IsPlaying: true }) { try { _element.Play(); } catch { } _playing = true; return; }
                    try { _element.Pause(); } catch { }
                    _playing = false;
                };
                freeze.Start();
                return;
            }
            // The live frame only covered the decoder while it opened the file; the clip is
            // drawing now, so put it away so nothing stale shows through.
            if (_live.Source is not null) _live.IsVisible = false;
            Apply();
        };
        _element.MediaEnded += (_, _) => Loop();
        // A decoder that fails mid-seek raises MediaFailed; unhandled, it takes the whole process
        // down. Catch it and blank the element instead, so the show keeps running.
        _element.MediaFailed += (_, _) => Fail();
        _picture.Children.Add(_element);
        _picture.Children.Add(_live);
        _picture.Children.Add(_replay);
        Children.Add(_picture);
        ApplyCrop();

        if (showPlaceholder)
        {
            _placeholder = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.MediaTag) ? "VIDEO" : $"VIDEO · {item.MediaTag.ToUpperInvariant()}",
                Foreground = Brushes.White,
                FontSize = Math.Max(9, item.FontSize * 0.6),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Opacity = 0.55,
                IsHitTestVisible = false
            };
            Children.Add(_placeholder);
        }

        UpdateChromaKey();
        if (_playback is not null) _playback.Changed += Apply;
        Apply();
    }

    /// <summary>
    /// Points an existing surface at a freshly edited element. Reusing the surface is what keeps a
    /// playing clip running when anything else in the studio changes; rebuilding it restarted the
    /// picture from frame one.
    /// </summary>
    public void SetElement(ScorebugElement item)
    {
        _item = item;
        ApplyCrop();
        ApplyStretch();
        UpdateChromaKey();
        if (_placeholder is not null)
            _placeholder.Text = string.IsNullOrWhiteSpace(item.MediaTag) ? "VIDEO" : $"VIDEO · {item.MediaTag.ToUpperInvariant()}";
        Apply();
    }

    /// <summary>
    /// Points the decoder at the element's chroma key, or clears the effect when the key is off.
    /// Linux has no WPF pixel shader: the key is a filter on the decoder's command line, so an
    /// already-open clip reopens once when the colour or tolerance changes.
    /// </summary>
    void UpdateChromaKey()
    {
        var key = (_item.ChromaKeyColor ?? "").Trim();
        var wanted = string.IsNullOrWhiteSpace(key) ? null : new ChromaKeyEffect
        {
            KeyColor = Paint.ColorOf(key, Colors.White),
            Tolerance = _item.ChromaKeyTolerance
        };
        if (string.Equals(wanted?.Filter, _element.ChromaKey?.Filter, StringComparison.Ordinal)) return;
        _element.ChromaKey = wanted;
        if (_loadedPath is not null) { try { _element.Close(); } catch { } _loadedPath = null; }
    }

    void SyncClip() => _clip.Rect = new Rect(0, 0, Math.Max(0, Bounds.Width), Math.Max(0, Bounds.Height));

    /// <summary>The source rectangle selected by the element's four crop percentages.</summary>
    Rect CropRect()
    {
        var left = Math.Clamp(_item.VideoCropLeft, 0, 0.49);
        var top = Math.Clamp(_item.VideoCropTop, 0, 0.49);
        var right = Math.Clamp(_item.VideoCropRight, 0, 0.49);
        var bottom = Math.Clamp(_item.VideoCropBottom, 0, 0.49);
        if (left + right > 0.9) right = Math.Max(0, 0.9 - left);
        if (top + bottom > 0.9) bottom = Math.Max(0, 0.9 - top);
        return new Rect(left, top, Math.Max(0.02, 1 - left - right), Math.Max(0.02, 1 - top - bottom));
    }

    bool IsCropped()
    {
        var crop = CropRect();
        return Math.Abs(crop.X) > 0.0001 || Math.Abs(crop.Y) > 0.0001
            || Math.Abs(crop.Width - 1) > 0.0001 || Math.Abs(crop.Height - 1) > 0.0001;
    }

    /// <summary>
    /// A cropped picture fills its box without distorting the remaining pixels. Cropping is a
    /// source cut, so the selected region is scaled proportionally until it covers the whole box;
    /// any small overflow is hidden by the element's clip. With no crop, Preserve still means
    /// uniform letterboxing.
    /// </summary>
    void ApplyStretch()
    {
        var stretch = IsCropped()
            ? Stretch.UniformToFill
            : _item.PreserveVideoAspect ? Stretch.Uniform : Stretch.UniformToFill;
        _element.Stretch = stretch;
        _live.Stretch = stretch;
        _replay.Stretch = stretch;
    }

    /// <summary>
    /// Shows only the selected fraction of the decoded picture. Every layer lives in the inner
    /// picture grid, so the file, the camera and the replay are cropped by the same transform.
    /// </summary>
    void ApplyCrop()
    {
        if (Bounds.Width < 1 || Bounds.Height < 1) return;
        var crop = CropRect();
        if (!IsCropped())
        {
            _picture.Width = double.NaN;
            _picture.Height = double.NaN;
            _picture.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            _picture.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
            _picture.RenderTransform = null;
            return;
        }
        _picture.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
        _picture.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        _picture.Width = Bounds.Width / crop.Width;
        _picture.Height = Bounds.Height / crop.Height;
        _picture.RenderTransform = new TranslateTransform(-crop.X * _picture.Width, -crop.Y * _picture.Height);
    }

    /// <summary>Crops a live or replay bitmap to the chosen source region.</summary>
    ImageSource CropFrame(ImageSource source)
    {
        var crop = CropRect();
        if (!IsCropped() || source is not BitmapSource bitmap) return source;
        try
        {
            var x = Math.Clamp((int)Math.Round(bitmap.PixelWidth * crop.X), 0, bitmap.PixelWidth - 1);
            var y = Math.Clamp((int)Math.Round(bitmap.PixelHeight * crop.Y), 0, bitmap.PixelHeight - 1);
            var width = Math.Clamp((int)Math.Round(bitmap.PixelWidth * crop.Width), 1, bitmap.PixelWidth - x);
            var height = Math.Clamp((int)Math.Round(bitmap.PixelHeight * crop.Height), 1, bitmap.PixelHeight - y);
            return new CroppedBitmap(bitmap, new Int32Rect(x, y, width, height));
        }
        catch { return source; }
    }

    /// <summary>
    /// The clip this surface should be showing. A muted surface is a preview (studio, program
    /// thumbnail), and only previews see a replay that has been opened but not started.
    /// </summary>
    MediaEntry? Cue() => _mute && !_onAir
        ? _playback?.PreviewFor(_item.MediaTag)
        : _playback?.ActiveFor(_item.MediaTag);

    /// <summary>The clip this surface resolves right now, exposed so a check can prove a monitor and
    /// a preview disagree about a staged replay.</summary>
    public MediaEntry? CurrentCue => Cue();

    /// <summary>
    /// A clip that loops behaves like a stadium screen: rewind and keep rolling. A clip with its
    /// loop switched off holds its last frame and lets the session move to the next playlist item.
    /// </summary>
    void Loop()
    {
        if (_loadedPath is null) { _playing = false; return; }
        var entry = Cue();
        if (entry is null || !entry.Loop || _playback is { IsPlaying: false }) { _playing = false; return; }
        try
        {
            _element.Position = TimeSpan.Zero;
            _element.Play();
            _playing = true;
        }
        catch { _playing = false; }
    }

    void Apply()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Apply); return; }
        SyncClip();
        ApplyCrop();
        var entry = Cue();
        // A cut to another clip or camera can carry its own transition. Snapshot the outgoing
        // picture before any layer is switched, then let it leave over the incoming source.
        var cueKey = entry is null ? ""
            : entry.IsCamera ? $"camera:{entry.CameraIndex}"
            : $"file:{entry.Path}";
        if (_cueKey is not null && !string.Equals(_cueKey, cueKey, StringComparison.OrdinalIgnoreCase))
            BeginSwitchAnimation(entry);
        _cueKey = cueKey;

        // A replay is decoded to frames by the session, not opened as a media file: the recorded
        // clips are MJPG AVI, which a decoder may show as one still frame.
        if (ShowReplayFrames(entry)) return;

        // A live camera has no file to open: its frames replace the MediaElement until the
        // operator cuts to an instant replay or another clip.
        if (entry is { IsCamera: true })
        {
            // A live feed has no file for the router to open, and a file must never keep playing
            // underneath the camera when the same index was already selected.
            StopRouting();
            try { _element.Stop(); _element.Close(); _element.Source = null; } catch { }
            _loadedPath = null;
            _opened = false;
            _playing = false;
            ShowLiveCamera(entry.CameraIndex);
            if (_placeholder is not null) _placeholder.IsVisible = false;
            return;
        }
        HideLiveCamera();

        var matches = entry is not null && File.Exists(entry.Path);

        if (!matches)
        {
            StopRouting();
            _live.IsVisible = false;
            if (_loadedPath is not null)
            {
                // Stop, Close and then clear the source. A MediaElement that is only stopped can
                // keep painting its last frame — which is exactly what stayed on the program
                // preview after Stop — so it is also taken out of the picture entirely.
                try { _element.Stop(); _element.Close(); _element.Source = null; } catch { }
                _loadedPath = null;
                _opened = false;
            }
            _playing = false;
            // Nothing is cued, so the box shows the artwork behind it, not the last video frame.
            _element.IsVisible = false;
            if (_placeholder is not null) _placeholder.IsVisible = true;
            return;
        }

        _element.IsVisible = true;
        if (_placeholder is not null) _placeholder.IsVisible = false;

        // Each clip carries its own level, and a clip routed to a chosen speaker (on the clip or on
        // this element) is muted here so its sound never comes out of two places at once.
        try
        {
            var busGain = Math.Clamp(_playback?.SpeakerGain ?? 1, 0, 1);
            _element.IsMuted = _mute || !string.IsNullOrWhiteSpace(_item.AudioDevice)
                || (_playback?.RoutedToDevice ?? false) || busGain <= 0.001;
            _element.Volume = (float)Math.Clamp((_playback?.Volume ?? 1) * busGain, 0, 1);
        }
        catch { }
        SyncRouting(entry!);

        if (_loadedPath != entry!.Path)
        {
            // A queued freeze from the previous cue must not touch this one.
            _freezeToken++;
            // Opening a file takes a few frames, during which the MediaElement is black. Hold the
            // last live picture over it so the scorebug never blinks out between live and replay.
            if (_live.Source is not null) _live.IsVisible = true;
            try
            {
                _element.Source = new Uri(entry.Path);
                _loadedPath = entry.Path;
                _playing = false;
                _appliedSeek = _playback?.SeekVersion ?? 0;
                if (_playback is null || _playback.IsPlaying) { _element.Play(); _playing = true; }
            }
            catch
            {
                _loadedPath = null;
                _playing = false;
                if (_placeholder is not null) _placeholder.IsVisible = true;
            }
            return;
        }

        // Same clip already loaded: follow the transport instead of restarting the picture.
        // ScrubbingEnabled draws the frame at the new position even while paused, so the element
        // needs no Play/Pause nudge here — which also keeps scrubbing smooth.
        if (_playback is not null && _playback.SeekVersion != _appliedSeek)
        {
            _appliedSeek = _playback.SeekVersion;
            _router?.Seek(_playback.Position);
            var resume = _playing && _playback.IsPlaying;
            try
            {
                if (resume) { _element.Pause(); _playing = false; }
                _element.Position = TimeSpan.FromSeconds(Math.Max(0, _playback.Position));
                if (resume)
                {
                    // Resume on the next dispatcher turn. Calling Play() in the same tick as
                    // Position is what some decoders do not survive (the seek that closed the app).
                    _resumeQueued = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        _resumeQueued = false;
                        if (_playback is not { IsPlaying: true }) return;
                        try { _element.Play(); _playing = true; } catch { }
                    });
                    return;
                }
            }
            catch { }
        }

        var desired = _playback?.IsPlaying ?? true;
        if (desired == _playing || _resumeQueued) return;
        try
        {
            if (desired)
            {
                _element.Play();
                _playing = true;
                // The clip is drawing again; the last live frame was only a cover while it opened.
                if (_live.Source is not null) _live.IsVisible = false;
            }
            else
            {
                _element.Pause();
                _playing = false;
            }
        }
        catch { }
        if (desired) _router?.Resume(); else _router?.Pause();
    }

    /// <summary>
    /// Plays (or pauses, seeks) this element's clip on its own speaker. Doing nothing when the
    /// element has no speaker is deliberate: that case keeps the MediaElement's sound.
    /// </summary>
    void SyncRouting(MediaEntry entry)
    {
        // Only a surface that is actually on an audience output plays sound. Otherwise the studio
        // and every preview would open their own copy of the same speaker and triple the audio.
        if (_mute)
        {
            StopRouting();
            return;
        }
        var device = _item.AudioDevice;
        if (string.IsNullOrWhiteSpace(device))
        {
            StopRouting();
            return;
        }
        _router ??= new AudioRouter();
        if (!string.Equals(_routedPath, entry.Path, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_routedDevice, device, StringComparison.OrdinalIgnoreCase))
        {
            // Start at the transport's point so joining a running clip stays in sync.
            var start = _playback?.Position ?? 0;
            var gain = Math.Clamp(_playback?.SpeakerGain ?? 1, 0, 1);
            _router.Play(entry.Path, device, Math.Clamp((_playback?.Volume ?? entry.Volume) * gain, 0, 1), start);
            _routedPath = entry.Path;
            _routedDevice = device;
        }
        if (_playback is { IsPlaying: false }) _router.Pause(); else _router.Resume();
        if (_playback is not null)
            _router.SetVolume(Math.Clamp(_playback.Volume * Math.Clamp(_playback.SpeakerGain, 0, 1), 0, 1));
    }

    void StopRouting()
    {
        if (_router is null) return;
        _router.Stop();
        _routedPath = null;
        _routedDevice = null;
    }

    void ShowLiveCamera(int index)
    {
        if (_cameraIndex != index)
        {
            UnsubscribeLive();
            _cameraIndex = index;
            if (_loadedPath is not null)
            {
                try { _element.Stop(); _element.Source = null; } catch { }
                _loadedPath = null;
                _opened = false;
                _playing = false;
            }
            _feed = CameraFeed.Get(index);
            _feed.FrameReady += OnCameraFrame;
            _feed.Start();
        }
        // The last frame stays in the image: cutting back from a replay shows the camera instantly
        // instead of a blank box until the next frame arrives a fraction of a second later.
        _live.IsVisible = true;
        _replay.IsVisible = false;
        _element.IsVisible = false;
    }

    /// <summary>Stops listening to the feed but keeps its last frame on screen.</summary>
    void UnsubscribeLive()
    {
        if (_feed is not null) _feed.FrameReady -= OnCameraFrame;
        _feed = null;
        _cameraIndex = -1;
        _liveFrame = null;
        _live.IsVisible = false;
        _element.IsVisible = true;
    }

    void OnCameraFrame(ImageSource frame)
    {
        _liveFrame = frame as BitmapSource;
        _live.Source = frame;
    }

    void HideLiveCamera()
    {
        if (_cameraIndex < 0) return;
        UnsubscribeLive();
    }

    /// <summary>
    /// Draws a replay's decoded frames and reports false for every other cue, which also releases
    /// the subscription so a surface never keeps a picture from a clip it no longer shows.
    /// </summary>
    bool ShowReplayFrames(MediaEntry? entry)
    {
        var feed = _playback?.ReplayFramesFor(_mute && !_onAir);
        if (entry is null || feed is null || !feed.Contains(entry.Path))
        {
            StopReplayFrames();
            return false;
        }
        HideLiveCamera();
        StopRouting();
        if (!ReferenceEquals(_replayFeed, feed))
        {
            StopReplayFrames();
            _replayFeed = feed;
            feed.FrameReady += OnReplayFrame;
        }
        if (_loadedPath is not null)
        {
            // The file is never opened here, so nothing stays on the player underneath.
            try { _element.Stop(); _element.Close(); _element.Source = null; } catch { }
            _loadedPath = null;
            _opened = false;
        }
        _playing = false;
        _element.IsVisible = false;
        if (_placeholder is not null) _placeholder.IsVisible = false;
        if (feed.Frame is { } picture)
        {
            _replayFrame = picture;
            _replay.Source = picture;
        }
        _replay.IsVisible = true;
        return true;
    }

    void StopReplayFrames()
    {
        if (_replayFeed is not null) _replayFeed.FrameReady -= OnReplayFrame;
        _replayFeed = null;
        if (_replay.IsVisible) _replay.IsVisible = false;
        _replay.Source = null;
        _replayFrame = null;
    }

    void OnReplayFrame(BitmapSource frame)
    {
        _replayFrame = frame;
        if (_replay.IsVisible) _replay.Source = frame;
    }

    /// <summary>
    /// Holds a still of the outgoing picture over the new source and animates it away. The still
    /// is important for camera cuts: the old frame is already visible while the next camera opens,
    /// so a fade, slide or zoom reads as a deliberate switch rather than a flash of black.
    /// </summary>
    void BeginSwitchAnimation(MediaEntry? incoming)
    {
        var transition = MediaLibrary.NormalizeTransition(incoming?.Transition);
        var seconds = MediaLibrary.NormalizeTransitionSeconds(incoming?.TransitionSeconds ?? MediaLibrary.DefaultTransitionSeconds);
        // A new cut always replaces an animation that has not finished yet.
        if (_switchOverlay is not null)
        {
            Children.Remove(_switchOverlay);
            _switchOverlay = null;
            _switchToken++;
        }
        if (transition == "Cut" || seconds <= 0.02 || !IsVisible) return;
        var width = (int)Math.Round(Bounds.Width);
        var height = (int)Math.Round(Bounds.Height);
        if (width < 2 || height < 2) return;
        RenderTargetBitmap snapshot;
        try
        {
            snapshot = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            snapshot.Render(this);
        }
        catch { return; }
        var overlay = new Image
        {
            Source = snapshot,
            Width = Bounds.Width,
            Height = Bounds.Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        Children.Add(overlay);
        _switchOverlay = overlay;
        var token = ++_switchToken;
        var started = DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromSeconds(seconds);
        if (transition == "Zoom")
            overlay.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var progress = Math.Clamp((DateTimeOffset.UtcNow - started).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            var eased = progress * progress * (3 - 2 * progress);
            overlay.Opacity = 1 - eased;
            if (transition == "Slide") overlay.RenderTransform = new TranslateTransform(width * eased, 0);
            else if (transition == "Zoom") overlay.RenderTransform = new ScaleTransform(1 + 0.14 * eased, 1 + 0.14 * eased);
            if (progress < 1) return;
            timer.Stop();
            if (token != _switchToken || !ReferenceEquals(_switchOverlay, overlay)) return;
            Children.Remove(overlay);
            _switchOverlay = null;
        };
        timer.Start();
    }

    /// <summary>Blanks the element after a decode failure instead of letting it crash the app.</summary>
    void Fail()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Fail); return; }
        _playing = false;
        _opened = false;
        _loadedPath = null;
        try { _element.Stop(); _element.Close(); _element.Source = null; } catch { }
        _element.IsVisible = false;
        _live.IsVisible = false;
        if (_placeholder is not null) _placeholder.IsVisible = true;
    }

    public void Dispose()
    {
        if (_playback is not null) _playback.Changed -= Apply;
        StopReplayFrames();
        _switchToken++;
        if (_switchOverlay is not null) { Children.Remove(_switchOverlay); _switchOverlay = null; }
        StopRouting();
        _router?.Dispose();
        _router = null;
        HideLiveCamera();
        _playing = false;
        _opened = false;
        try { _element.Stop(); _element.Source = null; } catch { }
    }
}
