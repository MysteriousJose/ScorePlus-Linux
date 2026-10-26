using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Scoreboard.Desktop;

/// <summary>
/// One media library and one transport for the whole session. The main window and every detached
/// control panel host their own <see cref="MediaDock"/> against this same object, so a clip cued on
/// one panel is exactly what the other panels and the scorebug see.
/// </summary>
public sealed class MediaSession
{
    readonly UserPreferences _preferences;
    readonly MediaPlayer _player = new();
    readonly DispatcherTimer _transport = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly Dictionary<string, ImageSource> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _pendingThumbs = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> _thumbAttempts = new(StringComparer.OrdinalIgnoreCase);

    bool _playing;
    bool _scrubbing;
    double _duration;

    public MediaSession(UserPreferences preferences)
    {
        _preferences = preferences;
        Playback = new MediaPlayback();
        foreach (var entry in preferences.Media)
            if (!string.IsNullOrWhiteSpace(entry.Path) && !Library.Any(m => string.Equals(m.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
                Library.Add(entry with { Tag = MediaLibrary.NormalizeTag(entry.Tag) });

        _player.MediaOpened += (_, _) =>
        {
            _duration = _player.NaturalDuration.HasTimeSpan ? Math.Max(0, _player.NaturalDuration.TimeSpan.TotalSeconds) : 0;
            TransportChanged?.Invoke();
            if (_playing) _player.Play();
        };
        _player.MediaEnded += (_, _) => SetTransportPlaying(false);
        _player.MediaFailed += (_, e) =>
        {
            SetTransportPlaying(false);
            Status?.Invoke($"MEDIA ERROR · {e.ErrorException?.Message ?? "this machine cannot decode that file"}");
        };
        _transport.Tick += (_, _) =>
        {
            if (_scrubbing) return;
            TransportChanged?.Invoke();
        };
        _transport.Start();
    }

    /// <summary>Clips in the library, in import order. Shared by every dock.</summary>
    public ObservableCollection<MediaEntry> Library { get; } = [];

    /// <summary>The clip that scorebug video elements watch, including its paused/running state.</summary>
    public MediaPlayback Playback { get; }

    public MediaEntry? Selected { get; private set; }

    public MediaEntry? Current { get; private set; }

    public bool IsPlaying => _playing;

    public double Position
    {
        get
        {
            try { return Math.Max(0, _player.Position.TotalSeconds); } catch { return 0; }
        }
    }

    public double Duration => _duration;

    /// <summary>Raised when the library or the selection changes.</summary>
    public event Action? StateChanged;

    /// <summary>Raised as the playhead moves, so only the transport redraws on every tick.</summary>
    public event Action? TransportChanged;

    /// <summary>Raised when a thumbnail becomes available for the given path.</summary>
    public event Action<string>? ThumbnailReady;

    /// <summary>Status-bar style message for whichever dock is active.</summary>
    public event Action<string>? Status;

    public ImageSource? Thumbnail(MediaEntry entry)
    {
        if (_thumbs.TryGetValue(entry.Path, out var cached)) return cached;
        QueueThumbnail(entry);
        return null;
    }

    public bool HasThumbnail(MediaEntry entry) => _thumbs.ContainsKey(entry.Path);

    public void BeginScrub() => _scrubbing = true;

    public void EndScrub() => _scrubbing = false;

    public void Seek(double seconds)
    {
        if (_player.Source is null) return;
        try { _player.Position = TimeSpan.FromSeconds(Math.Max(0, seconds)); } catch { }
        TransportChanged?.Invoke();
    }

    public void Import(IEnumerable<string> paths, string tag)
    {
        var normalized = MediaLibrary.NormalizeTag(tag);
        var added = 0;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (Library.Any(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            Library.Add(new MediaEntry(path, normalized));
            added++;
        }
        Save();
        StateChanged?.Invoke();
        Status?.Invoke(added == 0
            ? "MEDIA · those clips are already in the library"
            : $"MEDIA · {Library.Count} clip{(Library.Count == 1 ? "" : "s")} · {added} new in {normalized}");
    }

    public void Remove(MediaEntry entry)
    {
        Library.Remove(entry);
        if (ReferenceEquals(Selected, entry)) Selected = null;
        if (ReferenceEquals(Current, entry)) Stop();
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"REMOVED · {entry.Label}");
    }

    public void Retag(MediaEntry entry, string? tag)
    {
        var normalized = MediaLibrary.NormalizeTag(tag);
        if (entry.Tag == normalized)
        {
            Selected = entry;
            StateChanged?.Invoke();
            return;
        }
        var updated = MediaLibrary.Retag(Library, entry, normalized);
        if (updated is null) return;
        if (ReferenceEquals(Current, entry))
        {
            Current = updated;
            if (updated.IsVideo) Playback.Start(updated);
        }
        Selected = updated;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"TAG · {updated.Label} → {normalized}");
    }

    public void Select(MediaEntry entry)
    {
        Selected = entry;
        StateChanged?.Invoke();
        Status?.Invoke($"{entry.KindLabel} · {entry.Label} · tag {entry.Tag}");
    }

    /// <summary>Cues a clip: opens it, plays it and tells every video element on the scorebug.</summary>
    public void Play(MediaEntry entry)
    {
        if (!File.Exists(entry.Path))
        {
            Status?.Invoke("MEDIA FILE IS MISSING");
            return;
        }
        try { _player.Close(); } catch { }
        Selected = entry;
        Current = entry;
        _duration = 0;
        // Video sound lives on the audience output; muting here avoids hearing it twice.
        _player.Volume = entry.IsVideo ? 0 : 1;
        _player.Open(new Uri(entry.Path));
        _player.Play();
        SetTransportPlaying(true);
        // Audio-only clips must not fill a video overlay; videos cue the matching scorebug elements.
        if (entry.IsVideo) Playback.Start(entry); else Playback.Stop();
        StateChanged?.Invoke();
        Status?.Invoke($"PLAYING · {(entry.IsVideo ? "VIDEO" : "AUDIO")} · {entry.Label} · tag {entry.Tag}");
    }

    public void TogglePlayPause()
    {
        if (_playing)
        {
            try { _player.Pause(); } catch { }
            SetTransportPlaying(false);
            Status?.Invoke($"PAUSED · {Current?.Label ?? "clip"} · scorebug video holds its frame");
            return;
        }
        if (Selected is not null && !ReferenceEquals(Selected, Current)) { Play(Selected); return; }
        if (Current is null) { Status?.Invoke("SELECT A CLIP FIRST"); return; }
        _player.Volume = Current.IsVideo ? 0 : 1;
        try { _player.Play(); } catch { }
        SetTransportPlaying(true);
        if (Current.IsVideo) Playback.Start(Current);
        Status?.Invoke($"PLAYING · {(Current.IsVideo ? "VIDEO" : "AUDIO")} · {Current.Label}");
    }

    void SetTransportPlaying(bool playing)
    {
        _playing = playing;
        Playback.SetPlaying(playing);
        StateChanged?.Invoke();
        TransportChanged?.Invoke();
    }

    public void Stop()
    {
        try { _player.Stop(); _player.Close(); } catch { }
        Current = null;
        _duration = 0;
        SetTransportPlaying(false);
        _player.Volume = 1;
        Playback.Stop();
        StateChanged?.Invoke();
        Status?.Invoke("MEDIA STOPPED");
    }

    public void Save()
    {
        _preferences.Media = Library.ToList();
        try { PreferencesStore.Save(_preferences); } catch { }
    }

    // ---------- thumbnails ----------

    /// <summary>
    /// Grabs a frame a moment after the decoder opens the file. A MediaPlayer has no picture at
    /// MediaOpened yet, which is why a naive grab produced blank tiles; this seeks in, plays briefly,
    /// then renders. Failures (missing codec) fall back to the tile glyph.
    /// </summary>
    void QueueThumbnail(MediaEntry entry)
    {
        if (!entry.IsVideo || _thumbs.ContainsKey(entry.Path) || _pendingThumbs.Contains(entry.Path)) return;
        if (!File.Exists(entry.Path)) return;
        var attempt = _thumbAttempts.TryGetValue(entry.Path, out var count) ? count : 0;
        if (attempt >= 2) return;
        _thumbAttempts[entry.Path] = attempt + 1;
        _pendingThumbs.Add(entry.Path);

        var player = new MediaPlayer { Volume = 0, ScrubbingEnabled = true };
        var finished = false;
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };

        void Finish(bool captured)
        {
            if (finished) return;
            finished = true;
            delay.Stop();
            guard.Stop();
            try
            {
                if (captured)
                {
                    const int width = 240, height = 135;
                    var visual = new DrawingVisual();
                    using (var context = visual.RenderOpen()) context.DrawVideo(player, new Rect(0, 0, width, height));
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    bitmap.Freeze();
                    if (IsUsable(bitmap))
                    {
                        _thumbs[entry.Path] = bitmap;
                        ThumbnailReady?.Invoke(entry.Path);
                    }
                    else if (attempt + 1 < 2 && !_thumbs.ContainsKey(entry.Path))
                    {
                        _pendingThumbs.Remove(entry.Path);
                        QueueThumbnail(entry);
                    }
                }
            }
            catch { }
            finally
            {
                _pendingThumbs.Remove(entry.Path);
                try { player.Stop(); player.Close(); } catch { }
            }
        }

        player.MediaOpened += (_, _) =>
        {
            try
            {
                var total = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : TimeSpan.FromSeconds(3);
                var at = total > TimeSpan.FromSeconds(1.5) ? TimeSpan.FromSeconds(Math.Min(2, total.TotalSeconds / 3)) : TimeSpan.Zero;
                player.Position = at;
                player.Play();
                player.Pause();
                player.Play();
                delay.Start();
            }
            catch { Finish(false); }
        };
        delay.Tick += (_, _) => { delay.Stop(); player.Pause(); Finish(true); };
        guard.Tick += (_, _) => Finish(false);
        player.MediaFailed += (_, _) => Finish(false);

        guard.Start();
        try { player.Open(new Uri(entry.Path)); } catch { Finish(false); }
    }

    /// <summary>A frame that is one flat colour (usually black) means the decoder had nothing yet.</summary>
    static bool IsUsable(BitmapSource bitmap)
    {
        try
        {
            const int sample = 16;
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            var first = (pixels[0], pixels[1], pixels[2]);
            for (var y = 0; y < bitmap.PixelHeight; y += bitmap.PixelHeight / 4 + 1)
                for (var x = 0; x < bitmap.PixelWidth; x += sample)
                {
                    var i = y * stride + x * 4;
                    if (Math.Abs(pixels[i] - first.Item1) > 8 || Math.Abs(pixels[i + 1] - first.Item2) > 8 || Math.Abs(pixels[i + 2] - first.Item3) > 8)
                        return true;
                }
            return false;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _transport.Stop();
        try { _player.Close(); } catch { }
    }
}
