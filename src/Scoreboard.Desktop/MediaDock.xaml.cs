using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Scoreboard.Desktop;

/// <summary>
/// The one media dock. The main window and each detached control panel host their own copy of this
/// control against the same <see cref="MediaSession"/>, so imports, tags and playback stay in step.
/// Docks are hosted in resizable panels by their windows; the dock itself just fills what it is given.
/// Its palette follows the operator theme, so it sits in the app rather than on top of it.
/// </summary>
public partial class MediaDock : UserControl
{
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
        var selected = _session.Selected;
        if (selected is not null && !TagBox.IsKeyboardFocused && TagBox.Text != selected.Tag) TagBox.Text = selected.Tag;
        RefreshTiles();
        RefreshTransport();
    }

    void RefreshTiles()
    {
        if (_session is null) return;
        Tiles.Children.Clear();
        EmptyText.Visibility = _session.Library.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in _session.Library) Tiles.Children.Add(BuildTile(entry));
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
            ToolTip = $"{entry.Label}\n{entry.KindLabel} · tag {entry.Tag}\nDouble-click to play"
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
            Text = entry.Tag.ToUpperInvariant(),
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
        _session.Import(dialog.FileNames, MediaLibrary.NormalizeTag(TagBox.Text));
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.Selected is not { } selected) return;
        _session.Remove(selected);
    }

    void TagApply_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.Selected is not { } selected) return;
        _session.Retag(selected, TagBox.Text);
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
