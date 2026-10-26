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
    readonly ScorebugElement _item;
    readonly MediaPlayback? _playback;
    readonly MediaElement _element;
    readonly TextBlock _placeholder;
    readonly RectangleGeometry _clip = new();
    string? _loadedPath;
    bool _playing;

    public VideoSurface(ScorebugElement item, MediaPlayback? playback, bool mute)
    {
        _item = item;
        _playback = playback;
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
            ScrubbingEnabled = false,
            ClipToBounds = true
        };
        // Re-apply once the MediaElement is in the tree so Play() is honoured.
        _element.Loaded += (_, _) => { _loadedPath = null; _playing = false; Apply(); };
        // A newly opened clip starts from the top; a paused cue holds its very first frame.
        _element.MediaOpened += (_, _) =>
        {
            try { _element.Position = TimeSpan.Zero; } catch { }
            if (_playback is { IsPlaying: false } && !_playing)
            {
                // Start decoding, then freeze immediately so the pause state still shows a picture.
                try { _element.Play(); _element.Pause(); } catch { }
                return;
            }
            Apply();
        };
        _element.MediaEnded += (_, _) => Loop();
        Children.Add(_element);

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

        if (_playback is not null) _playback.Changed += Apply;
        Apply();
    }

    void SyncClip() => _clip.Rect = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));

    /// <summary>A stadium screen never stops on a black frame: rewind and keep rolling while cued.</summary>
    void Loop()
    {
        if (_loadedPath is null) { _playing = false; return; }
        if (_playback is { IsPlaying: false }) { _playing = false; return; }
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
        var matches = entry is not null && File.Exists(entry.Path);

        if (!matches)
        {
            if (_loadedPath is not null)
            {
                try { _element.Stop(); _element.Source = null; } catch { }
                _loadedPath = null;
            }
            _playing = false;
            _placeholder.Visibility = Visibility.Visible;
            return;
        }

        _placeholder.Visibility = Visibility.Collapsed;

        if (_loadedPath != entry!.Path)
        {
            try
            {
                _element.Source = new Uri(entry.Path);
                _loadedPath = entry.Path;
                _playing = false;
                if (_playback is null || _playback.IsPlaying) { _element.Play(); _playing = true; }
            }
            catch
            {
                _loadedPath = null;
                _playing = false;
                _placeholder.Visibility = Visibility.Visible;
            }
            return;
        }

        // Same clip already loaded: follow the transport instead of restarting the picture.
        var desired = _playback?.IsPlaying ?? true;
        if (desired == _playing) return;
        try
        {
            if (desired) { _element.Play(); _playing = true; }
            else { _element.Pause(); _playing = false; }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_playback is not null) _playback.Changed -= Apply;
        _playing = false;
        try { _element.Stop(); _element.Source = null; } catch { }
    }
}
