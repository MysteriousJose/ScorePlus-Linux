using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scoreboard.Core;

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
    // Live camera frames are drawn on this image instead of the MediaElement.
    readonly Image _live = new() { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
    readonly bool _mute;
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

    /// <param name="showPlaceholder">True only in the studio. The audience output and the preview
    /// stay blank while nothing is cued, so an off-air "VIDEO · GENERAL" label can never go out.</param>
    public VideoSurface(ScorebugElement item, MediaPlayback? playback, bool mute, bool showPlaceholder = true)
    {
        _item = item;
        _playback = playback;
        _mute = mute;
        ClipToBounds = true;
        // MediaElement's video texture can paint past its layout slot when the aspect ratios differ,
        // so the picture is clipped to the element box explicitly instead of relying on layout only.
        Clip = _clip;
        SizeChanged += (_, _) => SyncClip();
        _element = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.UniformToFill,
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
            if (resume <= 0.05) resume = _playback?.Position ?? 0;
            try { _element.Position = TimeSpan.FromSeconds(Math.Max(0, resume)); } catch { }
            _appliedSeek = _playback?.SeekVersion ?? 0;
            if (_playback is { IsPlaying: false } && !_playing)
            {
                // Start decoding, then freeze on the opening frame so a paused cue still shows art.
                try { _element.Play(); } catch { }
                Dispatcher.BeginInvoke(() => { try { _element.Pause(); } catch { } _playing = false; });
                return;
            }
            Apply();
        };
        _element.MediaEnded += (_, _) => Loop();
        // A decoder that fails mid-seek raises MediaFailed; unhandled, it takes the whole process
        // down. Catch it and blank the element instead, so the show keeps running.
        _element.MediaFailed += (_, _) => Fail();
        Children.Add(_element);
        Children.Add(_live);

        if (showPlaceholder)
        {
            _placeholder = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.MediaTag) ? "VIDEO" : $"VIDEO · {item.MediaTag.ToUpperInvariant()}",
                Foreground = Brushes.White,
                FontSize = Math.Max(9, item.FontSize * 0.6),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.55,
                IsHitTestVisible = false
            };
            Children.Add(_placeholder);
        }

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
        if (_placeholder is not null)
            _placeholder.Text = string.IsNullOrWhiteSpace(item.MediaTag) ? "VIDEO" : $"VIDEO · {item.MediaTag.ToUpperInvariant()}";
        Apply();
    }

    void SyncClip() => _clip.Rect = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));

    /// <summary>
    /// A clip that loops behaves like a stadium screen: rewind and keep rolling. A clip with its
    /// loop switched off holds its last frame and lets the session move to the next playlist item.
    /// </summary>
    void Loop()
    {
        if (_loadedPath is null) { _playing = false; return; }
        var entry = _playback?.ActiveFor(_item.MediaTag);
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
        var entry = _playback?.ActiveFor(_item.MediaTag);

        // A live camera has no file to open: its frames replace the MediaElement until the
        // operator cuts to an instant replay or another clip.
        if (entry is { IsCamera: true })
        {
            ShowLiveCamera(entry.CameraIndex);
            if (_placeholder is not null) _placeholder.Visibility = Visibility.Collapsed;
            return;
        }
        HideLiveCamera();

        var matches = entry is not null && File.Exists(entry.Path);

        if (!matches)
        {
            if (_loadedPath is not null)
            {
                try { _element.Stop(); _element.Source = null; } catch { }
                _loadedPath = null;
                _opened = false;
            }
            _playing = false;
            if (_placeholder is not null) _placeholder.Visibility = Visibility.Visible;
            return;
        }

        if (_placeholder is not null) _placeholder.Visibility = Visibility.Collapsed;

        // Each clip carries its own level, and a clip routed to a chosen speaker is muted here so
        // its sound never comes out of two places at once.
        try
        {
            _element.IsMuted = _mute || (_playback?.RoutedToDevice ?? false);
            _element.Volume = (float)Math.Clamp(_playback?.Volume ?? 1, 0, 1);
        }
        catch { }

        if (_loadedPath != entry!.Path)
        {
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
                if (_placeholder is not null) _placeholder.Visibility = Visibility.Visible;
            }
            return;
        }

        // Same clip already loaded: follow the transport instead of restarting the picture.
        // ScrubbingEnabled draws the frame at the new position even while paused, so the element
        // needs no Play/Pause nudge here — which also keeps scrubbing smooth.
        if (_playback is not null && _playback.SeekVersion != _appliedSeek)
        {
            _appliedSeek = _playback.SeekVersion;
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
            if (desired) { _element.Play(); _playing = true; }
            else { _element.Pause(); _playing = false; }
        }
        catch { }
    }

    void ShowLiveCamera(int index)
    {
        if (_cameraIndex != index)
        {
            HideLiveCamera();
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
        _live.Visibility = Visibility.Visible;
        _element.Visibility = Visibility.Collapsed;
    }

    void OnCameraFrame(ImageSource frame) => _live.Source = frame;

    void HideLiveCamera()
    {
        if (_cameraIndex < 0) return;
        if (_feed is not null) _feed.FrameReady -= OnCameraFrame;
        _feed = null;
        _cameraIndex = -1;
        _live.Source = null;
        _live.Visibility = Visibility.Collapsed;
        _element.Visibility = Visibility.Visible;
    }

    /// <summary>Blanks the element after a decode failure instead of letting it crash the app.</summary>
    void Fail()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Fail); return; }
        _playing = false;
        _opened = false;
        _loadedPath = null;
        try { _element.Stop(); _element.Source = null; } catch { }
        if (_placeholder is not null) _placeholder.Visibility = Visibility.Visible;
    }

    public void Dispose()
    {
        if (_playback is not null) _playback.Changed -= Apply;
        HideLiveCamera();
        _playing = false;
        _opened = false;
        try { _element.Stop(); _element.Source = null; } catch { }
    }
}
