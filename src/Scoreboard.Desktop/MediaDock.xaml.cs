using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Scoreboard.Desktop;

/// <summary>
/// The one media dock. The main window and each detached control panel host their own copy of this
/// control against the same <see cref="MediaSession"/>, so imports, folders, tags, playlists and
/// playback stay in step. Docks are hosted in resizable panels by their windows; the dock itself
/// just fills what it is given. Its palette follows the operator theme.
/// </summary>
public partial class MediaDock : UserControl
{
    const string AllFolders = "All folders";

    static readonly Brush BorderSelected = Frozen("#38D7A3");
    static readonly Brush BorderOnAir = Frozen("#FFD166");

    Brush _tilePanel = Frozen("#10151D");
    Brush _tileBorderIdle = Frozen("#1E2630");
    Brush _thumbBackground = Frozen("#0A0F16");
    Brush _tileText = Frozen("#D6DCE5");
    Brush _muted = Frozen("#93A0B2");

    MediaSession? _session;
    bool _showHints;
    bool _scrubbing;
    // Guards the bar's own Value writes. Without it, a focused bar turns every transport update
    // into another seek, which re-enters this method and storms the decoder until it dies.
    bool _syncingBar;
    // Guards combo writes made by Refresh so they are not read back as operator input.
    bool _syncing;
    string? _syncedSelection;
    string? _playlistNamesSignature;
    string? _selectedPlaylistName;
    // While a seek is being applied the bar must not snap back to the old position.
    DateTime _settleUntil;

    public MediaDock()
    {
        InitializeComponent();
        // The dock paints itself from this palette; ThemeManager must not repaint parts of it.
        ThemeManager.SetSkipTheme(this, true);
        TagBox.ItemsSource = MediaLibrary.SuggestedTags;
        TagBox.Text = MediaLibrary.DefaultTag;
        ApplyTheme("Dark");
    }

    /// <summary>Matches the dock to the operator theme (Dark is near-black, Light is white).</summary>
    public void ApplyTheme(string theme)
    {
        var light = theme == "Light";
        Set("DockPanel", light ? "#FFFFFF" : "#080808");
        Set("DockSurface", light ? "#E5EAF0" : "#141414");
        Set("DockBorder", light ? "#B9C3CE" : "#383838");
        Set("DockText", light ? "#18202B" : "#F4F7FB");
        Set("DockMuted", light ? "#586576" : "#93A0B2");
        Set("DockInput", light ? "#FFFFFF" : "#050505");
        Set("DockTrack", light ? "#C9D2DC" : "#26313D");
        Set("DockThumb", light ? "#FFFFFF" : "#EAF0F7");
        _tilePanel = Frozen(light ? "#F2F5F9" : "#10151D");
        _tileBorderIdle = Frozen(light ? "#C9D2DC" : "#1E2630");
        _thumbBackground = Frozen(light ? "#E2E8EF" : "#0A0F16");
        _tileText = Frozen(light ? "#18202B" : "#D6DCE5");
        _muted = Frozen(light ? "#586576" : "#93A0B2");
        Refresh();
    }

    void Set(string key, string hex) => Resources[key] = Frozen(hex);

    public void Attach(MediaSession session)
    {
        if (_session is not null)
        {
            _session.StateChanged -= Refresh;
            _session.TransportChanged -= RefreshTransport;
            _session.ThumbnailReady -= ThumbnailReady;
        }
        _session = session;
        session.StateChanged += Refresh;
        session.TransportChanged += RefreshTransport;
        session.ThumbnailReady += ThumbnailReady;
        Refresh();
    }

    public void SetHintVisibility(bool show)
    {
        _showHints = show;
        HintText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void ThumbnailReady(string _) => RefreshTiles();

    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_session is null) return;
        _syncing = true;
        try
        {
            var selected = _session.Selected;

            // ---------- folder filter ----------
            var folders = new List<string> { AllFolders };
            folders.AddRange(_session.Folders);
            if (!FoldersMatch(folders))
            {
                FolderFilter.ItemsSource = folders;
            }
            var wanted = _session.FolderFilter is null ? AllFolders : _session.FolderFilter;
            if (!string.Equals(FolderFilter.SelectedItem as string, wanted, StringComparison.OrdinalIgnoreCase))
                FolderFilter.SelectedItem = folders.FirstOrDefault(f => string.Equals(f, wanted, StringComparison.OrdinalIgnoreCase)) ?? AllFolders;

            RefreshTiles();

            // ---------- selected clip ----------
            if (selected is not null && !string.Equals(_syncedSelection, selected.Path, StringComparison.OrdinalIgnoreCase))
            {
                _syncedSelection = selected.Path;
                if (!FolderBox.IsKeyboardFocusWithin) FolderBox.Text = selected.Folder;
                if (!TagBox.IsKeyboardFocusWithin) TagBox.Text = selected.Tag;
            }
            ClipLoopBox.IsChecked = selected?.Loop == true;
            ClipLoopBox.IsEnabled = selected is not null;
            ClipCountText.Text = _session.VisibleLibrary.Count == _session.Library.Count
                ? $"{_session.Library.Count} clip{(_session.Library.Count == 1 ? "" : "s")}"
                : $"{_session.VisibleLibrary.Count} of {_session.Library.Count}";

            // ---------- playlists ----------
            var names = _session.Playlists.Select(p => p.Name).ToArray();
            var signature = string.Join("|", names);
            if (signature != _playlistNamesSignature)
            {
                _playlistNamesSignature = signature;
                PlaylistBox.ItemsSource = names;
                if (!names.Contains(_selectedPlaylistName ?? "", StringComparer.OrdinalIgnoreCase))
                    _selectedPlaylistName = names.FirstOrDefault();
            }
            if (!PlaylistBox.IsKeyboardFocusWithin)
                PlaylistBox.SelectedItem = names.FirstOrDefault(n => string.Equals(n, _selectedPlaylistName, StringComparison.OrdinalIgnoreCase));
            var playlist = ChosenPlaylist();
            PlaylistLoopBox.IsChecked = playlist?.Loop == true;
            PlaylistLoopBox.IsEnabled = playlist is not null;
            PlaylistInfo.Text = playlist is null
                ? "no playlist selected"
                : _session.ActivePlaylist?.Name == playlist.Name
                    ? $"playing {_session.PlaylistPosition + 1}/{playlist.Count}{(playlist.Loop ? " · loop" : "")}"
                    : $"{playlist.Count} clip{(playlist.Count == 1 ? "" : "s")}{(playlist.Loop ? " · loop" : "")}";
        }
        finally { _syncing = false; }

        RefreshTransport();
    }

    bool FoldersMatch(IReadOnlyList<string> folders)
    {
        if (FolderFilter.ItemsSource is not IEnumerable<string> existing) return false;
        return existing.SequenceEqual(folders, StringComparer.OrdinalIgnoreCase);
    }

    void RefreshTiles()
    {
        if (_session is null) return;
        Tiles.Children.Clear();
        var clips = _session.VisibleLibrary;
        EmptyText.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _session.Library.Count == 0
            ? "No media yet — Import adds anthems, ads and clips. Give each clip a tag so scorebug video elements know what to show."
            : "No clips in this folder — pick All folders, or import a clip here.";
        foreach (var entry in clips) Tiles.Children.Add(BuildTile(entry));
    }

    FrameworkElement BuildTile(MediaEntry entry)
    {
        var session = _session!;
        var isSelected = ReferenceEquals(entry, session.Selected);
        var isOnAir = ReferenceEquals(entry, session.Current);
        var tile = new Border
        {
            Width = 108,
            Height = 112,
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(8),
            Background = _tilePanel,
            BorderThickness = new Thickness(isSelected || isOnAir ? 2 : 1),
            BorderBrush = isOnAir ? BorderOnAir : isSelected ? BorderSelected : _tileBorderIdle,
            Cursor = Cursors.Hand,
            Tag = entry,
            ToolTip = $"{entry.Label}\n{entry.KindLabel} · tag {entry.Tag} · {entry.FolderLabel}{(entry.Loop ? "\nloops" : "")}\nDouble-click to play"
        };
        ThemeManager.SetSkipTheme(tile, true);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var thumb = new Grid { Background = _thumbBackground, ClipToBounds = true };
        var image = session.Thumbnail(entry);
        if (image is not null)
        {
            thumb.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill });
            if (isOnAir)
                thumb.Children.Add(new Border
                {
                    Background = Frozen("#B3000000"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Padding = new Thickness(5, 2, 5, 2),
                    CornerRadius = new CornerRadius(0, 0, 6, 0),
                    Child = new TextBlock { Text = session.IsPlaying ? "ON AIR" : "PAUSED", Foreground = BorderOnAir, FontSize = 9, FontWeight = FontWeights.Bold }
                });
        }
        else
        {
            thumb.Children.Add(new TextBlock
            {
                Text = entry.IsVideo ? "▶" : "♪",
                Style = (Style)Resources["TileGlyph"],
                FontSize = 22
            });
        }
        Grid.SetRow(thumb, 0);
        grid.Children.Add(thumb);

        var name = new TextBlock
        {
            Text = entry.Label,
            FontSize = 10,
            Foreground = _tileText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 5, 6, 0)
        };
        Grid.SetRow(name, 1);
        grid.Children.Add(name);

        var tag = new TextBlock
        {
            Text = (entry.Loop ? "⟳  " : "") + entry.TagAndFolder.ToUpperInvariant(),
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = _muted,
            Margin = new Thickness(6, 1, 6, 6),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(tag, 2);
        grid.Children.Add(tag);

        tile.Child = grid;
        tile.MouseLeftButtonDown += (_, e) =>
        {
            session.Select(entry);
            if (e.ClickCount == 2) session.Play(entry);
        };
        return tile;
    }

    void RefreshTransport()
    {
        if (_session is null) return;
        PlayPauseButton.Content = _session.IsPlaying ? "❚❚" : "▶";
        ElapsedText.Text = Stamp(TimeSpan.FromSeconds(_session.Position));
        var duration = _session.Duration;
        DurationText.Text = duration > 0 ? Stamp(TimeSpan.FromSeconds(duration)) : "—";
        // Never move the bar under the operator: while they hold it, or while it has focus for
        // arrow keys, the bar is the input, not the readout.
        if (_scrubbing || _syncingBar || DateTime.UtcNow < _settleUntil || ScrubBar.IsMouseCaptureWithin || ScrubBar.IsKeyboardFocusWithin) return;
        // Both writes are ours; the flag stops a coerced value from being read as a seek request.
        _syncingBar = true;
        try
        {
            ScrubBar.Maximum = Math.Max(1, duration);
            ScrubBar.Value = Math.Min(ScrubBar.Maximum, _session.Position);
        }
        finally { _syncingBar = false; }
    }

    MediaPlaylist? ChosenPlaylist()
    {
        if (_session is null) return null;
        var name = (PlaylistBox.SelectedItem as string) ?? _selectedPlaylistName ?? PlaylistBox.Text;
        return _session.Playlists.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- handlers ----------

    void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import media",
            Multiselect = true,
            Filter = "Audio & video|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.m4v;*.mp3;*.wav;*.m4a;*.aac;*.wma|Video (*.mp4;*.mov;*.mkv;*.webm)|*.mp4;*.mov;*.mkv;*.webm|Audio (*.mp3;*.wav;*.m4a;*.aac)|*.mp3;*.wav;*.m4a;*.aac|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        var folder = _session.FolderFilter ?? FolderBox.Text;
        _session.Import(dialog.FileNames, MediaLibrary.NormalizeTag(TagBox.Text), folder);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.Selected is not { } selected) return;
        _session.Remove(selected);
    }

    void FolderFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _session is null || FolderFilter.SelectedItem is not string choice) return;
        _session.SetFolderFilter(choice == AllFolders ? null : choice);
    }

    void FolderMove_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.Selected is not { } selected) return;
        _session.MoveToFolder(selected, FolderBox.Text);
    }

    void TagApply_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.Selected is not { } selected) return;
        _session.Retag(selected, TagBox.Text);
    }

    void ClipLoop_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing || _session?.Selected is not { } selected) return;
        _session.SetClipLoop(selected, ClipLoopBox.IsChecked == true);
    }

    void PlaylistBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        _selectedPlaylistName = PlaylistBox.SelectedItem as string ?? _selectedPlaylistName;
        Refresh();
    }

    void PlaylistNew_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var name = (PlaylistBox.Text ?? "").Trim();
        if (name.Length == 0) name = $"Playlist {_session.Playlists.Count + 1}";
        var playlist = _session.CreatePlaylist(name);
        _selectedPlaylistName = playlist.Name;
        Refresh();
    }

    void PlaylistAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var playlist = ChosenPlaylist();
        if (playlist is null)
        {
            playlist = _session.CreatePlaylist(PlaylistBox.Text);
            _selectedPlaylistName = playlist.Name;
        }
        if (_session.Selected is not { } selected)
        {
            _session.Report("SELECT A CLIP FIRST");
            return;
        }
        _session.AddToPlaylist(playlist, selected);
        Refresh();
    }

    void PlaylistRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.Selected is not { } selected || ChosenPlaylist() is not { } playlist) return;
        _session.RemoveFromPlaylist(playlist, selected);
        Refresh();
    }

    void PlaylistPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || ChosenPlaylist() is not { } playlist) return;
        _session.PlayPlaylist(playlist);
        Refresh();
    }

    void PlaylistStop_Click(object sender, RoutedEventArgs e) => _session?.Stop();

    void PlaylistLoop_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing || _session is null || ChosenPlaylist() is not { } playlist) return;
        _session.SetPlaylistLoop(playlist, PlaylistLoopBox.IsChecked == true);
        Refresh();
    }

    void PlayPause_Click(object sender, RoutedEventArgs e) => _session?.TogglePlayPause();

    void Stop_Click(object sender, RoutedEventArgs e) => _session?.Stop();

    void Scrub_Start(object sender, MouseButtonEventArgs e)
    {
        _scrubbing = true;
        _session?.BeginScrub();
    }

    void Scrub_End(object sender, MouseButtonEventArgs e)
    {
        if (_session is null) { _scrubbing = false; return; }
        _session.Seek(ScrubBar.Value);
        _session.EndScrub();
        _scrubbing = false;
        // Hold the bar at the requested point until the player has actually moved there.
        _settleUntil = DateTime.UtcNow.AddMilliseconds(450);
    }

    /// <summary>A click on the bar, a drag of the thumb and the arrow keys all land here.</summary>
    void Scrub_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_session is null || _syncingBar) return;
        if (!_scrubbing && !ScrubBar.IsKeyboardFocusWithin) return;
        _session.Seek(e.NewValue);
    }

    static string Stamp(TimeSpan value) => $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";

    static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
