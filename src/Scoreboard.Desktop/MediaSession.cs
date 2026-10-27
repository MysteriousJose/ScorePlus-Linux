using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// One media library and one transport for the whole session. The main window and every detached
/// control panel host their own <see cref="MediaDock"/> against this same object, so a clip cued on
/// one panel is exactly what the other panels and the scorebug see.
/// Clips live in folders, route to the scorebug by tag, loop individually, and can be collected
/// into playlists that run one after another.
/// </summary>
public sealed class MediaSession
{
    readonly UserPreferences _preferences;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    // ScrubbingEnabled is what makes Position stick while the clip is paused; without it the
    // transport can only be watched from the beginning.
    readonly MediaPlayer _player = new() { ScrubbingEnabled = true };
    readonly DispatcherTimer _transport = new() { Interval = TimeSpan.FromMilliseconds(200) };
    // Scrub bursts are coalesced into a single seek: pause/seek/play on every pixel of a drag is
    // what made seeking feel laggy.
    readonly DispatcherTimer _seekApply = new() { Interval = TimeSpan.FromMilliseconds(80) };
    readonly Dictionary<string, ImageSource> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _pendingThumbs = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> _thumbAttempts = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _folders = [];

    bool _playing;
    bool _scrubbing;
    double _duration;
    // A seek asked for before the decoder opened is replayed as soon as it does.
    double? _pendingSeek;
    double _seekTarget;
    MediaPlaylist? _playlist;
    int _playlistIndex = -1;

    public MediaSession(UserPreferences preferences)
    {
        _preferences = preferences;
        Playback = new MediaPlayback();
        foreach (var entry in preferences.Media)
            if (!string.IsNullOrWhiteSpace(entry.Path) && !Library.Any(m => string.Equals(m.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
                Library.Add(entry with { Tag = MediaLibrary.NormalizeTag(entry.Tag), Folder = MediaLibrary.NormalizeFolder(entry.Folder) });
        foreach (var playlist in preferences.Playlists)
            if (!string.IsNullOrWhiteSpace(playlist.Name))
                Playlists.Add(new MediaPlaylist { Name = playlist.Name.Trim(), Loop = playlist.Loop, Paths = playlist.Paths.ToList() });
        foreach (var folder in preferences.MediaFolders)
        {
            var normalized = MediaLibrary.NormalizeFolder(folder);
            if (normalized.Length > 0 && !_folders.Contains(normalized, StringComparer.OrdinalIgnoreCase)) _folders.Add(normalized);
        }

        _player.MediaOpened += (_, _) =>
        {
            _duration = _player.NaturalDuration.HasTimeSpan ? Math.Max(0, _player.NaturalDuration.TimeSpan.TotalSeconds) : 0;
            if (_pendingSeek is double pending)
            {
                _pendingSeek = null;
                try { _player.Position = TimeSpan.FromSeconds(Math.Clamp(pending, 0, Math.Max(0, _duration))); } catch { }
            }
            TransportChanged?.Invoke();
            if (_playing) _player.Play();
        };
        _player.MediaEnded += (_, _) => HandleClipEnded();
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
        _seekApply.Tick += (_, _) => { _seekApply.Stop(); ApplySeek(); };
    }

    /// <summary>Clips in the library, in import order. Shared by every dock.</summary>
    public ObservableCollection<MediaEntry> Library { get; } = [];

    /// <summary>Saved playlists, shared by every dock.</summary>
    public ObservableCollection<MediaPlaylist> Playlists { get; } = [];

    /// <summary>The clip that scorebug video elements watch, including its paused/running state.</summary>
    public MediaPlayback Playback { get; }

    public MediaEntry? Selected { get; private set; }

    public MediaEntry? Current { get; private set; }

    /// <summary>The playlist currently running, or null when a clip was cued on its own.</summary>
    public MediaPlaylist? ActivePlaylist => _playlist;

    public int PlaylistPosition => _playlistIndex;

    /// <summary>Folder shown in the docks; null means every folder. Shared so all panels agree.</summary>
    public string? FolderFilter { get; private set; }

    /// <summary>The clips the dock should show, honouring the folder filter.</summary>
    public IReadOnlyList<MediaEntry> VisibleLibrary =>
        FolderFilter is null
            ? Library.ToArray()
            : Library.Where(e => string.Equals(MediaLibrary.NormalizeFolder(e.Folder), FolderFilter, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>Every folder on the shelf: ones that hold clips plus ones created empty.</summary>
    public IReadOnlyList<string> Folders => _folders.Concat(MediaLibrary.Folders(Library))
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();

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

    /// <summary>Lets a dock post a message without raising the event itself.</summary>
    public void Report(string message) => Status?.Invoke(message);

    public ImageSource? Thumbnail(MediaEntry entry)
    {
        if (_thumbs.TryGetValue(entry.Path, out var cached)) return cached;
        QueueThumbnail(entry);
        return null;
    }

    public bool HasThumbnail(MediaEntry entry) => _thumbs.ContainsKey(entry.Path);

    public void BeginScrub() => _scrubbing = true;

    public void EndScrub() => _scrubbing = false;

    /// <summary>Adds a folder to the shelf even before anything is filed in it.</summary>
    public string CreateFolder(string? folder)
    {
        var normalized = MediaLibrary.NormalizeFolder(folder);
        if (normalized.Length == 0) return "";
        if (_folders.Contains(normalized, StringComparer.OrdinalIgnoreCase)) return normalized;
        _folders.Add(normalized);
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"FOLDER CREATED · {normalized}");
        return normalized;
    }

    /// <summary>
    /// Clears the organisation for a new game: no folders, no playlists, every clip unfiled. The
    /// imported media itself is kept — a new game is a blank slate for the production, not a reason
    /// to throw away the operator's asset library.
    /// </summary>
    public void ResetOrganization()
    {
        Stop(keepStatus: true);
        Playlists.Clear();
        _folders.Clear();
        for (var index = 0; index < Library.Count; index++)
            if (Library[index].Folder.Length > 0) Library[index] = Library[index] with { Folder = "" };
        FolderFilter = null;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke("MEDIA · new game · folders and playlists cleared");
    }

    public void SetFolderFilter(string? folder)
    {
        var normalized = folder is null ? null : MediaLibrary.NormalizeFolder(folder);
        if (string.Equals(FolderFilter, normalized, StringComparison.OrdinalIgnoreCase)) return;
        FolderFilter = normalized;
        StateChanged?.Invoke();
    }

    public void Seek(double seconds)
    {
        var target = Math.Max(0, seconds);
        if (_duration > 0) target = Math.Min(target, _duration);
        _seekTarget = target;
        _seekApply.Stop();
        _seekApply.Start();
        TransportChanged?.Invoke();
    }

    /// <summary>
    /// Notes the last media operation in <c>logs\media-last-action.txt</c>. A decoder that dies
    /// natively leaves no managed traceback; this file says exactly which step it died on.
    /// </summary>
    static void Trace(string message)
    {
        try
        {
            var folder = AppPaths.Combine("logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "media-last-action.txt"), $"{DateTimeOffset.Now:O} {message}");
        }
        catch { /* tracing must never interfere with playback */ }
    }

    /// <summary>Applies the latest requested point once the drag has settled.</summary>
    void ApplySeek()
    {
        var target = _seekTarget;
        _pendingSeek = target;
        Trace($"seek {target:0.00}s · {Current?.Label ?? "no clip"} · transport start");
        if (_player.Source is not null)
        {
            try
            {
                // A seek is applied most reliably while paused, so the playing clip is briefly held.
                var resume = _playing;
                if (resume) _player.Pause();
                _player.Position = TimeSpan.FromSeconds(target);
                _pendingSeek = null;
                // Resume one turn later: Play() in the same tick as Position can kill the decoder.
                if (resume) _ui.BeginInvoke(() => { try { _player.Play(); } catch { } });
                Trace($"seek {target:0.00}s · transport moved");
            }
            catch { }
        }
        // The picture the operator watches is the scorebug video, not this muted transport player.
        Trace($"seek {target:0.00}s · publishing to the scorebug ({Playback.NowPlaying?.Label ?? "nothing"})");
        Playback.Seek(target);
        TransportChanged?.Invoke();
        Trace($"seek {target:0.00}s · done");
    }

    public void Import(IEnumerable<string> paths, string tag, string? folder = null)
    {
        var normalized = MediaLibrary.NormalizeTag(tag);
        var folderName = MediaLibrary.NormalizeFolder(folder);
        var added = 0;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (Library.Any(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            // A video overlay loops like a stadium screen until the operator says otherwise.
            Library.Add(new MediaEntry(path, normalized, folderName, Loop: MediaLibrary.IsVideoFile(path)));
            added++;
        }
        Save();
        StateChanged?.Invoke();
        Status?.Invoke(added == 0
            ? "MEDIA · those clips are already in the library"
            : $"MEDIA · {Library.Count} clip{(Library.Count == 1 ? "" : "s")} · {added} new in {normalized}{(folderName.Length == 0 ? "" : $" / {folderName}")}");
    }

    public void Remove(MediaEntry entry)
    {
        Library.Remove(entry);
        foreach (var playlist in Playlists) playlist.Paths.RemoveAll(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase));
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
        ReplaceCurrent(entry, updated);
        Selected = updated;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"TAG · {updated.Label} → {normalized}");
    }

    /// <summary>Moves the selected clip into a folder (or the unfiled bucket with an empty name).</summary>
    public void MoveToFolder(MediaEntry entry, string? folder)
    {
        var normalized = MediaLibrary.NormalizeFolder(folder);
        if (MediaLibrary.NormalizeFolder(entry.Folder) == normalized)
        {
            Selected = entry;
            StateChanged?.Invoke();
            return;
        }
        var updated = MediaLibrary.MoveToFolder(Library, entry, normalized);
        if (updated is null) return;
        ReplaceCurrent(entry, updated);
        Selected = updated;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"FOLDER · {updated.Label} → {updated.FolderLabel}");
    }

    /// <summary>Turns this one clip's own loop on or off.</summary>
    public void SetClipLoop(MediaEntry entry, bool loop)
    {
        var updated = MediaLibrary.SetLoop(Library, entry, loop);
        if (updated is null) return;
        ReplaceCurrent(entry, updated);
        Selected = updated;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"LOOP {(loop ? "ON" : "OFF")} · {updated.Label}");
    }

    /// <summary>Keeps the cued clip pointing at the updated record after a tag/folder/loop edit.</summary>
    void ReplaceCurrent(MediaEntry before, MediaEntry after)
    {
        if (!ReferenceEquals(Current, before)) return;
        Current = after;
        if (!after.IsVideo) return;
        // Retagging or re-filing the clip that is on air must not restart it from frame one.
        var wasPlaying = Playback.IsPlaying;
        var position = Playback.Position;
        Playback.Start(after);
        if (position > 0) Playback.Seek(position);
        if (!wasPlaying) Playback.SetPlaying(false);
    }

    public void Select(MediaEntry entry)
    {
        Selected = entry;
        StateChanged?.Invoke();
        Status?.Invoke($"{entry.KindLabel} · {entry.Label} · tag {entry.Tag} · {entry.FolderLabel}");
    }

    /// <summary>Cues a clip: opens it, plays it and tells every video element on the scorebug.</summary>
    public void Play(MediaEntry entry) => PlayClip(entry, null, -1);

    void PlayClip(MediaEntry entry, MediaPlaylist? playlist, int index)
    {
        if (!File.Exists(entry.Path))
        {
            Status?.Invoke("MEDIA FILE IS MISSING");
            return;
        }
        _playlist = playlist;
        _playlistIndex = index;
        try { _player.Close(); } catch { }
        // A seek queued for the previous clip must not land on this one.
        _seekApply.Stop();
        _seekTarget = 0;
        Selected = entry;
        Current = entry;
        _duration = 0;
        _pendingSeek = null;
        // Video sound lives on the audience output; muting here avoids hearing it twice.
        _player.Volume = entry.IsVideo ? 0 : 1;
        _player.Open(new Uri(entry.Path));
        _player.Play();
        SetTransportPlaying(true);
        // Audio-only clips must not fill a video overlay; videos cue the matching scorebug elements.
        if (entry.IsVideo) Playback.Start(entry); else Playback.Stop();
        StateChanged?.Invoke();
        var where = playlist is null ? "" : $" · {playlist.Name} {index + 1}/{playlist.Count}";
        Status?.Invoke($"PLAYING · {(entry.IsVideo ? "VIDEO" : "AUDIO")} · {entry.Label} · tag {entry.Tag}{where}");
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

    /// <summary>What happens when the current clip reaches its end.</summary>
    void HandleClipEnded()
    {
        if (Current is null)
        {
            SetTransportPlaying(false);
            return;
        }
        // A playlist owns the order: clips run one after another whether or not they loop alone.
        if (_playlist is not null)
        {
            var next = _playlistIndex + 1;
            if (next < _playlist.Count) { PlayPlaylistIndex(next); return; }
            if (_playlist.Loop) { PlayPlaylistIndex(0); return; }
            Status?.Invoke($"PLAYLIST ENDED · {_playlist.Name}");
            Stop(keepStatus: true);
            return;
        }
        if (Current.Loop)
        {
            try
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
            }
            catch { SetTransportPlaying(false); return; }
            Playback.Seek(0);
            SetTransportPlaying(true);
            return;
        }
        Status?.Invoke($"CLIP ENDED · {Current.Label}");
        Stop(keepStatus: true);
    }

    void PlayPlaylistIndex(int index)
    {
        if (_playlist is null || _playlist.Count == 0) { Stop(); return; }
        // Skip over clips that have been removed from the library.
        for (var step = 0; step < _playlist.Count; step++)
        {
            var probe = (index + step) % _playlist.Count;
            var path = _playlist.Paths[probe];
            var entry = Library.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry is not null) { PlayClip(entry, _playlist, probe); return; }
        }
        Status?.Invoke($"PLAYLIST IS EMPTY · {_playlist.Name}");
        Stop(keepStatus: true);
    }

    // ---------- playlists ----------

    public MediaPlaylist CreatePlaylist(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) trimmed = $"Playlist {Playlists.Count + 1}";
        var existing = Playlists.FirstOrDefault(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Status?.Invoke($"PLAYLIST EXISTS · {existing.Name}");
            return existing;
        }
        var playlist = new MediaPlaylist { Name = trimmed };
        Playlists.Add(playlist);
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"PLAYLIST CREATED · {playlist.Name}");
        return playlist;
    }

    public void DeletePlaylist(MediaPlaylist playlist)
    {
        if (ReferenceEquals(_playlist, playlist)) Stop(keepStatus: true);
        Playlists.Remove(playlist);
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"PLAYLIST REMOVED · {playlist.Name}");
    }

    public void AddToPlaylist(MediaPlaylist playlist, MediaEntry entry)
    {
        if (playlist.Paths.Any(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase)))
        {
            Status?.Invoke($"ALREADY IN {playlist.Name.ToUpperInvariant()} · {entry.Label}");
            return;
        }
        playlist.Paths.Add(entry.Path);
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"{playlist.Name.ToUpperInvariant()} · + {entry.Label} ({playlist.Count})");
    }

    public void RemoveFromPlaylist(MediaPlaylist playlist, MediaEntry entry)
    {
        var removed = playlist.Paths.RemoveAll(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"{playlist.Name.ToUpperInvariant()} · − {entry.Label}");
    }

    /// <summary>The playlist's clips in play order, skipping files that have left the library.</summary>
    public IReadOnlyList<MediaEntry> PlaylistEntries(MediaPlaylist playlist) =>
        playlist.Paths.Select(path => Library.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase)))
            .Where(entry => entry is not null).Select(entry => entry!).ToArray();

    public void ClearPlaylist(MediaPlaylist playlist)
    {
        playlist.Paths.Clear();
        Save();
        StateChanged?.Invoke();
    }

    public void SetPlaylistLoop(MediaPlaylist playlist, bool loop)
    {
        playlist.Loop = loop;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"{playlist.Name.ToUpperInvariant()} · LOOP {(loop ? "ON" : "OFF")}");
    }

    /// <summary>Starts the playlist at one of its clips, used by double-clicking in an open list.</summary>
    public void PlayPlaylistFrom(MediaPlaylist playlist, int index)
    {
        if (playlist.Count == 0)
        {
            Status?.Invoke($"PLAYLIST IS EMPTY · {playlist.Name}");
            return;
        }
        _playlist = playlist;
        PlayPlaylistIndex(Math.Clamp(index, 0, playlist.Count - 1));
    }

    /// <summary>Starts the playlist from its first clip.</summary>
    public void PlayPlaylist(MediaPlaylist playlist)
    {
        if (playlist.Count == 0)
        {
            Status?.Invoke($"PLAYLIST IS EMPTY · {playlist.Name}");
            return;
        }
        _playlist = playlist;
        PlayPlaylistIndex(0);
    }

    /// <summary>Skips to the next clip, inside a playlist or through the library.</summary>
    public void NextClip()
    {
        if (_playlist is not null) { PlayPlaylistIndex((_playlistIndex + 1) % _playlist.Count); return; }
        var items = VisibleLibrary;
        if (items.Count == 0) return;
        var index = Current is null ? -1 : items.ToList().FindIndex(m => string.Equals(m.Path, Current.Path, StringComparison.OrdinalIgnoreCase));
        Play(items[(index + 1 + items.Count) % items.Count]);
    }

    public void Stop(bool keepStatus = false)
    {
        try { _player.Stop(); _player.Close(); } catch { }
        _seekApply.Stop();
        _seekTarget = 0;
        Current = null;
        _duration = 0;
        _pendingSeek = null;
        _playlist = null;
        _playlistIndex = -1;
        SetTransportPlaying(false);
        _player.Volume = 1;
        Playback.Stop();
        StateChanged?.Invoke();
        if (!keepStatus) Status?.Invoke("MEDIA STOPPED");
    }

    public void Save()
    {
        _preferences.Media = Library.ToList();
        _preferences.Playlists = Playlists.Select(p => new MediaPlaylist { Name = p.Name, Loop = p.Loop, Paths = p.Paths.ToList() }).ToList();
        _preferences.MediaFolders = _folders.ToList();
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
        _seekApply.Stop();
        try { _player.Close(); } catch { }
    }
}
