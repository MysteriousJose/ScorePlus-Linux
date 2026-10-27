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
/// playback stay in step. Folders and playlists are shelves of thumbnails: a clip is filed by
/// dragging it onto a folder, and a playlist opens into its own clip list instead of a dropdown.
/// Docks are hosted in resizable panels by their windows; the dock itself just fills what it is given.
/// </summary>
public partial class MediaDock : UserControl
{
    enum MediaView { Library, Playlist }

    const string ClipFormat = "ScorePlus.MediaPath";

    static readonly Brush BorderSelected = Frozen("#38D7A3");
    static readonly Brush BorderOnAir = Frozen("#FFD166");

    Brush _tilePanel = Frozen("#10151D");
    Brush _tileBorderIdle = Frozen("#1E2630");
    Brush _thumbBackground = Frozen("#0A0F16");
    Brush _tileText = Frozen("#D6DCE5");
    Brush _muted = Frozen("#93A0B2");
    Brush _accentSoft = Frozen("#123C33");

    MediaSession? _session;
    string _theme = "Dark";
    bool _showHints;
    bool _scrubbing;
    // Guards the bar's own Value writes. Without it, a focused bar turns every transport update
    // into another seek, which re-enters this method and storms the decoder until it dies.
    bool _syncingBar;
    // Guards control writes made by Refresh so they are not read back as operator input.
    bool _syncing;
    string? _syncedSelection;
    // While a seek is being applied the bar must not snap back to the old position.
    DateTime _settleUntil;

    MediaView _view = MediaView.Library;
    string? _openPlaylistName;
    string _search = "";
    bool _syncingSearch;
    IReadOnlyList<MediaEntry> _shownClips = [];
    MediaPlaylist? _shownPlaylist;
    Point _dragOrigin;
    MediaEntry? _dragEntry;

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
        _theme = theme;
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
        _accentSoft = Frozen(light ? "#D9F4EC" : "#123C33");
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

    /// <summary>Returns every dock to the unfiltered library, used when a new game starts blank.</summary>
    public void ResetView()
    {
        ClearSearch(refresh: false);
        _view = MediaView.Library;
        _openPlaylistName = null;
        Refresh();
    }

    void ThumbnailReady(string _) => RefreshTiles(_shownClips, _shownPlaylist);

    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_session is null) return;
        if (_view == MediaView.Playlist && OpenPlaylist() is null)
        {
            _view = MediaView.Library;
            _openPlaylistName = null;
        }

        _syncing = true;
        try
        {
            BuildFolderRail();
            BuildPlaylistRail();

            var playlist = _view == MediaView.Playlist ? OpenPlaylist() : null;
            var inPlaylist = playlist is not null;
            FolderRailRow.Visibility = PlaylistRailRow.Visibility = inPlaylist ? Visibility.Collapsed : Visibility.Visible;
            BackToLibraryButton.Visibility = inPlaylist ? Visibility.Visible : Visibility.Collapsed;
            PlaylistActions.Visibility = inPlaylist ? Visibility.Visible : Visibility.Collapsed;
            PlaylistLoopBox.Visibility = inPlaylist ? Visibility.Visible : Visibility.Collapsed;
            RemoveButton.Content = inPlaylist ? "Remove clip" : "Remove";
            RemoveButton.ToolTip = inPlaylist
                ? "Take the selected clip out of this playlist (it stays in the library)"
                : "Remove the selected clip from the library";

            if (playlist is not null)
            {
                var entries = _session.PlaylistEntries(playlist);
                ViewTitle.Text = playlist.Name.ToUpperInvariant();
                ViewMeta.Text = $"{entries.Count} clip{(entries.Count == 1 ? "" : "s")} · {(playlist.Loop ? "loop" : "plays once")}";
                PlaylistLoopBox.IsChecked = playlist.Loop;
                PlaylistLoopBox.IsEnabled = true;
                PlaylistAddButton.IsEnabled = _session.Selected is not null;
                PlaylistRunButton.IsEnabled = entries.Count > 0;
                PlaylistStopButton.IsEnabled = _session.ActivePlaylist is not null;
                RefreshTiles(entries, playlist);
                ClipCountText.Text = $"{entries.Count} in playlist";
            }
            else
            {
                var folder = _session.FolderFilter;
                var visible = FilteredLibrary();
                ViewTitle.Text = Searching ? "SEARCH" : folder is null ? "LIBRARY" : folder.Length == 0 ? "UNFILED" : folder.ToUpperInvariant();
                ViewMeta.Text = Searching
                    ? $"{visible.Count} match{(visible.Count == 1 ? "" : "es")} · across every folder and playlist"
                    : _session.Library.Count == 0
                        ? "import a clip to begin"
                        : $"{visible.Count} of {_session.Library.Count} clips · drag a clip onto a folder or playlist";
                PlaylistLoopBox.IsChecked = false;
                PlaylistLoopBox.IsEnabled = false;
                RefreshTiles(visible, null);
                ClipCountText.Text = Searching
                    ? $"{visible.Count} match{(visible.Count == 1 ? "" : "es")}"
                    : $"{_session.Library.Count} clip{(_session.Library.Count == 1 ? "" : "s")}";
            }

            var selected = _session.Selected;
            if (selected is not null && !string.Equals(_syncedSelection, selected.Path, StringComparison.OrdinalIgnoreCase))
            {
                _syncedSelection = selected.Path;
                if (!TagBox.IsKeyboardFocusWithin) TagBox.Text = selected.Tag;
            }
            ClipLoopBox.IsChecked = selected?.Loop == true;
            ClipLoopBox.IsEnabled = selected is not null;
        }
        finally { _syncing = false; }

        RefreshTransport();
    }

    // ---------- search ----------

    bool Searching => _search.Length > 0;

    bool Matches(string? value) => Searching && !string.IsNullOrWhiteSpace(value)
        && value.Contains(_search, StringComparison.OrdinalIgnoreCase);

    /// <summary>While searching, the folder filter is irrelevant: every clip in every folder is fair.</summary>
    IReadOnlyList<MediaEntry> FilteredLibrary()
    {
        if (_session is null) return [];
        if (!Searching) return _session.VisibleLibrary;
        var fromMatchingPlaylists = _session.Playlists
            .Where(playlist => Matches(playlist.Name))
            .SelectMany(playlist => playlist.Paths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _session.Library.Where(entry =>
            Matches(entry.Label) || Matches(entry.Tag) || Matches(entry.Folder) || Matches(entry.FolderLabel)
            || Matches(entry.KindLabel) || fromMatchingPlaylists.Contains(entry.Path)).ToArray();
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearch) return;
        var query = (SearchBox.Text ?? "").Trim();
        var changed = !string.Equals(query, _search, StringComparison.Ordinal);
        _search = query;
        SearchPrompt.Visibility = Searching ? Visibility.Collapsed : Visibility.Visible;
        SearchClearButton.Visibility = Searching ? Visibility.Visible : Visibility.Collapsed;
        if (!changed) return;
        // Search is a library-wide view; leave an open playlist so nothing stays hidden behind it.
        if (Searching && _view == MediaView.Playlist)
        {
            _view = MediaView.Library;
            _openPlaylistName = null;
        }
        Refresh();
    }

    void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        ClearSearch();
        SearchBox.Focus();
    }

    void ClearSearch(bool refresh = true)
    {
        if (!Searching && SearchBox.Text.Length == 0) return;
        _syncingSearch = true;
        SearchBox.Text = "";
        _syncingSearch = false;
        _search = "";
        SearchPrompt.Visibility = Visibility.Visible;
        SearchClearButton.Visibility = Visibility.Collapsed;
        if (refresh) Refresh();
    }

    // ---------- shelves ----------

    void BuildFolderRail()
    {
        FolderRail.Children.Clear();
        if (_session is null) return;

        var folders = Searching ? _session.Folders.Where(Matches).ToArray() : _session.Folders.ToArray();
        if (!Searching)
        {
            FolderRail.Children.Add(BuildFolderTile(null, "ALL", _session.Library.Count, all: true, "Every clip in the library"));
            var unfiled = _session.Library.Count(e => MediaLibrary.NormalizeFolder(e.Folder).Length == 0);
            if (unfiled > 0)
                FolderRail.Children.Add(BuildFolderTile("", "UNFILED", unfiled, all: false, "Clips that are not in a folder"));
        }
        foreach (var folder in folders)
        {
            var count = _session.Library.Count(e => string.Equals(MediaLibrary.NormalizeFolder(e.Folder), folder, StringComparison.OrdinalIgnoreCase));
            FolderRail.Children.Add(BuildFolderTile(folder, folder.ToUpperInvariant(), count, all: false, $"Show the clips in {folder}. Drop a clip here to file it."));
        }
        if (Searching && folders.Length == 0) FolderRail.Children.Add(RailNote("NO FOLDERS MATCH"));
        if (!Searching) FolderRail.Children.Add(BuildNewShelfTile("+", "NEW FOLDER", "Create a folder", (_, _) => NewFolder()));
    }

    FrameworkElement RailNote(string text) => new TextBlock
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeights.Bold,
        Foreground = _muted,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 0, 8, 0)
    };

    FrameworkElement BuildFolderTile(string? filter, string label, int count, bool all, string tooltip)
    {
        var selected = all
            ? _session?.FolderFilter is null
            : string.Equals(_session?.FolderFilter, filter ?? "", StringComparison.OrdinalIgnoreCase);
        var tile = new Border
        {
            Width = 94,
            Height = 50,
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(7),
            Background = selected ? _accentSoft : _tilePanel,
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected ? BorderSelected : _tileBorderIdle,
            Cursor = Cursors.Hand,
            AllowDrop = true,
            Tag = filter,
            ToolTip = $"{tooltip}\n{count} clip{(count == 1 ? "" : "s")}\nDrop library clips or files here"
        };
        ThemeManager.SetSkipTheme(tile, true);

        var grid = new Grid { IsHitTestVisible = false, Margin = new Thickness(7, 5, 6, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(FolderGlyph(selected ? BorderSelected : BorderOnAir, 24, 18));

        var text = new StackPanel { Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = _tileText,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = $"{count} clip{(count == 1 ? "" : "s")}",
            FontSize = 9,
            Foreground = _muted,
            Margin = new Thickness(0, 1, 0, 0)
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        tile.Child = grid;

        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (_session is null) return;
            ClearSearch(refresh: false);
            _session.SetFolderFilter(filter);
            Refresh();
            e.Handled = true;
        };
        tile.DragEnter += (s, e) => HighlightDrop(s, e);
        tile.DragOver += (s, e) => HighlightDrop(s, e);
        tile.DragLeave += (s, e) => { if (s is Border border) border.BorderBrush = selected ? BorderSelected : _tileBorderIdle; };
        tile.Drop += (s, e) =>
        {
            e.Handled = true;
            if (_session is null) return;
            // Files dropped from Explorer join the library in this folder; library clips move here.
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                _session.Import(files, MediaLibrary.NormalizeTag(TagBox.Text), filter ?? "");
                Refresh();
                return;
            }
            var entry = DraggedEntry(e);
            if (entry is null) return;
            _session.MoveToFolder(entry, filter ?? "");
            Refresh();
        };
        return tile;
    }

    void BuildPlaylistRail()
    {
        PlaylistRail.Children.Clear();
        if (_session is null) return;
        var playlists = Searching ? _session.Playlists.Where(playlist => Matches(playlist.Name)).ToArray() : _session.Playlists.ToArray();
        foreach (var playlist in playlists)
        {
            var onAir = string.Equals(_session.ActivePlaylist?.Name, playlist.Name, StringComparison.OrdinalIgnoreCase);
            var tile = new Border
            {
                Width = 106,
                Height = 50,
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(7),
                Background = onAir ? _accentSoft : _tilePanel,
                BorderThickness = new Thickness(onAir ? 2 : 1),
                BorderBrush = onAir ? BorderOnAir : _tileBorderIdle,
                Cursor = Cursors.Hand,
                AllowDrop = true,
                Tag = playlist,
                ToolTip = $"Open {playlist.Name} ({playlist.Count} clip{(playlist.Count == 1 ? "" : "s")}){(playlist.Loop ? " · loop" : "")}\nDrop a clip or files here to add them, double-click to play."
            };
            ThemeManager.SetSkipTheme(tile, true);

            var grid = new Grid { IsHitTestVisible = false, Margin = new Thickness(7, 5, 6, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new TextBlock
            {
                Text = "≡",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = onAir ? BorderOnAir : BorderSelected,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 22
            });

            var text = new StackPanel { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = playlist.Name,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = _tileText,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{playlist.Count} clip{(playlist.Count == 1 ? "" : "s")}{(playlist.Loop ? " · ⟳" : "")}",
                FontSize = 9,
                Foreground = _muted,
                Margin = new Thickness(0, 1, 0, 0)
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            tile.Child = grid;

        tile.MouseLeftButtonDown += (_, e) =>
        {
            OpenPlaylist(playlist);
            if (e.ClickCount == 2)
            {
                _session?.PlayPlaylist(playlist);
                Refresh();
            }
            e.Handled = true;
        };
            tile.DragEnter += (s, e) => HighlightDrop(s, e);
            tile.DragOver += (s, e) => HighlightDrop(s, e);
            tile.DragLeave += (s, e) => { if (s is Border border) border.BorderBrush = onAir ? BorderOnAir : _tileBorderIdle; };
            tile.Drop += (s, e) =>
            {
                e.Handled = true;
                if (_session is null) return;
                // Files from Explorer are imported, then appended to the playlist in one step.
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    _session.Import(files, MediaLibrary.NormalizeTag(TagBox.Text));
                    foreach (var path in files)
                    {
                        var added = _session.Library.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
                        if (added is not null) _session.AddToPlaylist(playlist, added);
                    }
                    Refresh();
                    return;
                }
                var entry = DraggedEntry(e);
                if (entry is null) return;
                _session.AddToPlaylist(playlist, entry);
                Refresh();
            };
            PlaylistRail.Children.Add(tile);
        }
        if (Searching && playlists.Length == 0) PlaylistRail.Children.Add(RailNote("NO PLAYLISTS MATCH"));
        if (!Searching) PlaylistRail.Children.Add(BuildNewShelfTile("+", "NEW LIST", "Create a playlist", (_, _) => NewPlaylist()));
    }

    FrameworkElement BuildNewShelfTile(string glyph, string label, string tooltip, RoutedEventHandler click)
    {
        var tile = new Border
        {
            Width = 64,
            Height = 50,
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = _tileBorderIdle,
            Cursor = Cursors.Hand,
            ToolTip = tooltip
        };
        ThemeManager.SetSkipTheme(tile, true);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        stack.Children.Add(new TextBlock
        {
            Text = glyph,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = _muted,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = _muted,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        tile.Child = stack;
        tile.MouseLeftButtonDown += (_, e) => { click(tile, e); e.Handled = true; };
        return tile;
    }

    void HighlightDrop(object sender, DragEventArgs e)
    {
        var files = e.Data.GetDataPresent(DataFormats.FileDrop);
        if (!files && !e.Data.GetDataPresent(ClipFormat)) return;
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
        if (sender is Border border) border.BorderBrush = BorderSelected;
    }

    MediaEntry? DraggedEntry(DragEventArgs e)
    {
        if (_session is null || e.Data.GetData(ClipFormat) is not string path) return null;
        return _session.Library.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    static FrameworkElement FolderGlyph(Brush fill, double width, double height) => new System.Windows.Shapes.Path
    {
        Data = Geometry.Parse("M 1,4 L 8,4 L 10,7 L 23,7 L 23,19 L 1,19 Z"),
        Fill = fill,
        Width = width,
        Height = height,
        Stretch = Stretch.Fill,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    // ---------- clip tiles ----------

    void RefreshTiles(IReadOnlyList<MediaEntry>? clips, MediaPlaylist? playlist)
    {
        if (_session is null) return;
        var list = clips ?? _session.VisibleLibrary;
        _shownClips = list;
        _shownPlaylist = playlist;
        Tiles.Children.Clear();
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Searching
            ? $"Nothing matches “{_search}” in the library, folders or playlists."
            : playlist is not null
                ? _session.Library.Count == 0
                    ? "The library is empty — import a clip, then drag it onto this playlist."
                    : "This playlist is empty — go back to the library and drag a clip onto it, or select a clip and press +."
                : _session.Library.Count == 0
                    ? "No media yet — Import adds anthems, ads and clips. Give each clip a tag so scorebug video elements know what to show."
                    : "No clips in this folder — drag one here from All, or import a clip.";
        for (var index = 0; index < list.Count; index++)
            Tiles.Children.Add(BuildTile(list[index], playlist is null ? -1 : index, playlist));
    }

    FrameworkElement BuildTile(MediaEntry entry, int index, MediaPlaylist? playlist)
    {
        var session = _session!;
        var isSelected = ReferenceEquals(entry, session.Selected);
        var isOnAir = ReferenceEquals(entry, session.Current);
        var tile = new Border
        {
            Width = 108,
            Height = 116,
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(8),
            Background = _tilePanel,
            BorderThickness = new Thickness(isSelected || isOnAir ? 2 : 1),
            BorderBrush = isOnAir ? BorderOnAir : isSelected ? BorderSelected : _tileBorderIdle,
            Cursor = Cursors.Hand,
            Tag = entry,
            ToolTip = $"{entry.Label}\n{entry.KindLabel} · tag {entry.Tag} · {entry.FolderLabel}{(entry.Loop ? "\nloops" : "")}\n"
                + (playlist is null
                    ? "Double-click to play · drag onto a folder or playlist"
                    : $"Double-click to play from here in {playlist.Name} · drag onto a folder to re-file")
        };
        ThemeManager.SetSkipTheme(tile, true);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var thumb = new Grid { Background = _thumbBackground, ClipToBounds = true };
        var image = session.Thumbnail(entry);
        if (image is not null)
        {
            thumb.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill });
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
        if (isOnAir)
        {
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
        if (index >= 0)
        {
            thumb.Children.Add(new Border
            {
                Background = Frozen("#CC05080D"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(5, 1, 5, 1),
                CornerRadius = new CornerRadius(0, 0, 0, 6),
                Child = new TextBlock { Text = (index + 1).ToString(), Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold }
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
            Text = (entry.Loop ? "⟳  " : "") + (playlist is null ? entry.TagAndFolder : entry.Tag).ToUpperInvariant(),
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
            if (e.ClickCount == 2)
            {
                if (playlist is not null) session.PlayPlaylistFrom(playlist, index);
                else session.Play(entry);
            }
            e.Handled = true;
        };
        tile.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragEntry = entry;
            _dragOrigin = e.GetPosition(this);
        };
        tile.PreviewMouseLeftButtonUp += (_, _) => _dragEntry = null;
        tile.PreviewMouseMove += (_, e) =>
        {
            if (_dragEntry is null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { _dragEntry = null; return; }
            var position = e.GetPosition(this);
            if (Math.Abs(position.X - _dragOrigin.X) < 9 && Math.Abs(position.Y - _dragOrigin.Y) < 9) return;
            var path = _dragEntry.Path;
            _dragEntry = null;
            DragDrop.DoDragDrop(tile, new DataObject(ClipFormat, path), DragDropEffects.Move);
        };
        return tile;
    }

    // ---------- transport ----------

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

    // ---------- views ----------

    MediaPlaylist? OpenPlaylist()
    {
        if (_session is null || _openPlaylistName is null) return null;
        return _session.Playlists.FirstOrDefault(p => string.Equals(p.Name, _openPlaylistName, StringComparison.OrdinalIgnoreCase));
    }

    void OpenPlaylist(MediaPlaylist playlist)
    {
        ClearSearch(refresh: false);
        _view = MediaView.Playlist;
        _openPlaylistName = playlist.Name;
        Refresh();
    }

    // ---------- handlers ----------

    void BackToLibrary_Click(object sender, RoutedEventArgs e)
    {
        _view = MediaView.Library;
        _openPlaylistName = null;
        Refresh();
    }

    void NewFolder()
    {
        if (_session is null) return;
        var name = PromptName("New folder", "Name this folder. Clips can be dragged onto its tile at any time.", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var folder = _session.CreateFolder(name);
        if (folder.Length == 0) return;
        ClearSearch(refresh: false);
        _session.SetFolderFilter(folder);
        Refresh();
    }

    void NewPlaylist()
    {
        if (_session is null) return;
        var name = PromptName("New playlist", "Name this playlist. Drag clips onto its tile, or open it and add the selected clip.", $"Playlist {_session.Playlists.Count + 1}");
        if (string.IsNullOrWhiteSpace(name)) return;
        var playlist = _session.CreatePlaylist(name);
        ClearSearch(refresh: false);
        OpenPlaylist(playlist);
    }

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
        // Import into the folder on screen; "All" means unfiled.
        _session.Import(dialog.FileNames, MediaLibrary.NormalizeTag(TagBox.Text), _session.FolderFilter ?? "");
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.Selected is not { } selected) return;
        if (_view == MediaView.Playlist && OpenPlaylist() is { } playlist)
            _session.RemoveFromPlaylist(playlist, selected);
        else
            _session.Remove(selected);
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

    void PlaylistAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || OpenPlaylist() is not { } playlist) return;
        if (_session.Selected is not { } selected) { _session.Report("SELECT A CLIP FIRST"); return; }
        _session.AddToPlaylist(playlist, selected);
        Refresh();
    }

    void PlaylistRun_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || OpenPlaylist() is not { } playlist) return;
        _session.PlayPlaylist(playlist);
        Refresh();
    }

    void PlaylistStop_Click(object sender, RoutedEventArgs e) => _session?.Stop();

    void PlaylistDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || OpenPlaylist() is not { } playlist) return;
        _session.DeletePlaylist(playlist);
        _view = MediaView.Library;
        _openPlaylistName = null;
        Refresh();
    }

    void PlaylistLoop_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing || _session is null || OpenPlaylist() is not { } playlist) return;
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

    // ---------- small themed prompt ----------

    string? PromptName(string title, string hint, string initial)
    {
        var owner = Window.GetWindow(this);
        var window = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontFamily = new FontFamily("Segoe UI")
        };
        if (owner is not null) window.Owner = owner;

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(new TextBlock { Text = hint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 9) });
        var box = new TextBox { Text = initial, FontSize = 13, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0) };
        root.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 11, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 34, Padding = new Thickness(13, 6, 13, 6), Margin = new Thickness(4, 0, 0, 0) };
        var create = new Button
        {
            Content = "Create",
            IsDefault = true,
            MinHeight = 34,
            Padding = new Thickness(13, 6, 13, 6),
            Margin = new Thickness(4, 0, 0, 0),
            Background = Frozen("#0B6B50"),
            BorderBrush = Frozen("#14A67C"),
            Foreground = Brushes.White
        };
        ThemeManager.SetSkipTheme(create, true);
        string? result = null;
        create.Click += (_, _) => { result = box.Text.Trim(); window.DialogResult = true; };
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        root.Children.Add(buttons);
        window.Content = root;
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        ThemeManager.Apply(window, _theme);
        return window.ShowDialog() == true && !string.IsNullOrWhiteSpace(result) ? result : null;
    }

    static string Stamp(TimeSpan value) => $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";

    static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
