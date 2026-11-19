using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Controls;

using Avalonia.Media;
using Scoreboard.Core;
namespace Scoreboard.Desktop;
public partial class OutputWindow : Window
{
    readonly GameEngine _engine;
    readonly ScorebugCatalog _catalog;
    GameState _displayState;
    readonly ScorebugCanvasView _scorebugView;
    readonly AnnotationBoard _annotationBoard;
    readonly TelestratorOverlay _annotationInk;
    // The pen, highlighter and eraser live above the graphic, so a stroke never edits the layout.
    Grid? _annotationLayer;
    Grid? _annotationSurface;
    Border? _annotationToolbar;
    Border? _annotationSwatch;
    TextBox? _annotationWidthBox;
    ComboBox? _annotationFadePicker;
    TextBlock? _annotationStatus;
    bool _annotationToolsOpen;
    bool _settingAnnotationFade;
    // Which display this window is the audience output for. Only that display's published layout
    // is ever drawn here, so two screens can be on air with completely different designs.
    readonly string _displayId;
    string _broadcastTheme;
    SystemDecorations _style; WindowState _state; bool _full;
    double _left, _top, _width, _height;
    bool _geometrySaved;
    public OutputWindow(GameEngine engine, ScorebugCatalog catalog,string broadcastTheme="Dark",MediaPlayback? playback=null,string? displayId=null, bool annotations=false, AnnotationBoard? annotationBoard=null)
    {
        InitializeComponent(); _broadcastTheme=broadcastTheme; _engine = engine; _catalog=catalog; _displayState=engine.CloneState();
        _displayId=(catalog.FindDisplay(displayId)??catalog.Displays[0]).Id;
        // The audience only ever sees the layout that was published, never the one being designed.
        _scorebugView=new ScorebugCanvasView(catalog,engine,()=>_displayState,playback,muteVideo:false,layoutProvider:()=>_catalog.PublishedLayoutOf(_displayId),displayId:_displayId);
        ScorebugHost.Children.Add(_scorebugView);
        var saved = PreferencesStore.Load();
        _annotationBoard = annotationBoard ?? AnnotationBoard.ForDisplay(_displayId, TimeSpan.FromSeconds(saved.AnnotationFadeSeconds));
        _annotationInk = new TelestratorOverlay(_annotationBoard)
        {
            IsVisible = false,
            IsHitTestVisible = false,
            Width = Math.Max(1, _catalog.PublishedLayoutOf(_displayId).Width),
            Height = Math.Max(1, _catalog.PublishedLayoutOf(_displayId).Height)
        };
        _annotationInk.SetInk(Color.FromRgb(0xFF, 0xD1, 0x66), Math.Clamp(saved.AnnotationWidth, 1, 24));
        BuildAnnotationLayer();
        if (annotations) OpenAnnotationTools();
        _engine.Changed+=Update; _engine.Changed+=_scorebugView.Refresh; _catalog.Changed+=Publish; Update();
    }

    /// <summary>True while the pen/highlighter toolbar is available on this window.</summary>
    public bool AnnotationToolsOpen => _annotationToolsOpen;

    /// <summary>The pen width shown in the toolbar; also lets the checks verify typing a width.</summary>
    public double AnnotationWidth => _annotationInk.InkWidth;

    /// <summary>The design space the pen draws in; it follows the on-air layout.</summary>
    public Size AnnotationSurfaceSize => _annotationSurface is null
        ? new Size(0, 0)
        : new Size(_annotationSurface.Width, _annotationSurface.Height);

    /// <summary>How long ink stays before the quick fade; zero means permanent.</summary>
    public TimeSpan AnnotationFadeDelay => _annotationBoard.Hold;

    /// <summary>Shows the pen layer and lets the operator draw over the live graphic.</summary>
    public void OpenAnnotationTools()
    {
        BuildAnnotationLayer();
        _annotationBoard.Open();
        _annotationToolsOpen = true;
        if (_annotationLayer is not null) _annotationLayer.IsVisible = true;
        if (_annotationToolbar is not null) _annotationToolbar.IsVisible = true;
        _annotationInk.IsVisible = true;
        _annotationInk.IsHitTestVisible = true;
        _annotationInk.SetInk(_annotationInk.InkColor, PreferencesStore.Load().AnnotationWidth);
        SyncAnnotationWidthBox();
        SyncAnnotationFadePicker();
        SetAnnotationTool(AnnotationTool.Pen);
        _annotationInk.Focus();
    }

    /// <summary>Shows existing ink read-only, for a projector that should not draw.</summary>
    public void ShowAudienceInk()
    {
        BuildAnnotationLayer();
        _annotationBoard.Open();
        _annotationToolsOpen = false;
        if (_annotationLayer is not null) _annotationLayer.IsVisible = true;
        if (_annotationToolbar is not null) _annotationToolbar.IsVisible = false;
        _annotationInk.IsVisible = true;
        _annotationInk.IsHitTestVisible = false;
        _annotationInk.InvalidateVisual();
    }

    /// <summary>Ends the draw session and clears the ink from every output for this display.</summary>
    public void CloseAnnotationTools()
    {
        _annotationBoard.Dismiss();
        HideAnnotationLayer();
    }

    void HideAnnotationLayer()
    {
        _annotationToolsOpen = false;
        _annotationInk.ClearInk();
        _annotationInk.IsVisible = false;
        if (_annotationToolbar is not null) _annotationToolbar.IsVisible = false;
        if (_annotationLayer is not null) _annotationLayer.IsVisible = false;
        _annotationInk.InvalidateVisual();
    }

    /// <summary>
    /// Builds the toolbar and the ink surface. The ink is arranged in the scorebug's design space
    /// and stretched by its own Viewbox, so a stroke lands on the artwork on every output however
    /// the window is shaped instead of painting in the letterbox bars.
    /// </summary>
    void BuildAnnotationLayer()
    {
        if (_annotationLayer is not null || Content is not Grid root) return;
        var layout = _catalog.PublishedLayoutOf(_displayId);
        _annotationSurface = new Grid { Width = Math.Max(1, layout.Width), Height = Math.Max(1, layout.Height) };
        _annotationSurface.Children.Add(_annotationInk);
        _annotationLayer = new Grid { IsVisible = false };
        var inkHost = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
            Child = _annotationSurface
        };
        _annotationLayer.Children.Add(inkHost);
        var panel = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
        var bar = new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(232, 15, 24, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 69, 82)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 5, 8, 5),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(12),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = panel
            }
        };
        _annotationToolbar = bar;
        panel.Children.Add(ToolbarButton("Pen", (_, _) => SetAnnotationTool(AnnotationTool.Pen)));
        panel.Children.Add(ToolbarButton("Highlighter", (_, _) => SetAnnotationTool(AnnotationTool.Highlighter)));
        panel.Children.Add(ToolbarButton("Eraser", (_, _) => SetAnnotationTool(AnnotationTool.Eraser)));
        panel.Children.Add(ToolbarSeparator());
        var colour = ToolbarButton("Colour", (_, _) => ChooseAnnotationColour());
        _annotationSwatch = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(_annotationInk.InkColor),
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 3, 0)
        };
        colour.Content = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Children =
            {
                _annotationSwatch,
                new TextBlock { Text = "Colour", VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center }
            }
        };
        panel.Children.Add(colour);
        panel.Children.Add(ToolbarButton("−", (_, _) => ChangeAnnotationWidth(-1)));
        _annotationWidthBox = new TextBox
        {
            Text = _annotationInk.InkWidth.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            Width = 44, MinWidth = 44, MaxWidth = 44, Height = 24,
            FontSize = 11, Padding = new Thickness(2, 0, 2, 0),
            Margin = new Thickness(2, 0, 2, 0), TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(11, 17, 24)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 69, 82)),
            ToolTip = "Pen width — type 1–24 or use − / +"
        };
        _annotationWidthBox.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            CommitAnnotationWidth();
            e.Handled = true;
        };
        _annotationWidthBox.LostFocus += (_, _) => CommitAnnotationWidth();
        panel.Children.Add(_annotationWidthBox);
        panel.Children.Add(ToolbarButton("+", (_, _) => ChangeAnnotationWidth(1)));
        panel.Children.Add(ToolbarSeparator());
        panel.Children.Add(new TextBlock
        {
            Text = "Fade",
            Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)),
            FontSize = 10, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 4, 0)
        });
        _annotationFadePicker = new ComboBox
        {
            Width = 92, Height = 24, MinHeight = 24, FontSize = 10,
            Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(2, 0, 2, 0),
            VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            ItemsSource = new[] { "0.5 s", "1 s", "1.5 s", "2 s", "3 s", "5 s", "10 s", "Permanent" },
            SelectedItem = FadeLabel(_annotationBoard.Hold),
            ToolTip = "How long ink stays before it fades; Permanent keeps it until Clear"
        };
        _annotationFadePicker.SelectionChanged += (_, _) =>
        {
            if (_settingAnnotationFade || _annotationFadePicker.SelectedItem is not string label) return;
            var hold = label == "Permanent"
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(double.TryParse(label.Split(' ')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 1);
            _annotationBoard.SetHold(hold);
            SaveAnnotationPreferences();
        };
        panel.Children.Add(_annotationFadePicker);
        panel.Children.Add(ToolbarSeparator());
        panel.Children.Add(ToolbarButton("Undo", (_, _) => _annotationInk.UndoInk()));
        panel.Children.Add(ToolbarButton("Clear", (_, _) => _annotationInk.ClearInk()));
        panel.Children.Add(ToolbarButton("Done", (_, _) => CloseAnnotationTools()));
        _annotationStatus = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)),
            FontSize = 10, Margin = new Thickness(10, 0, 2, 0),
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };
        panel.Children.Add(_annotationStatus);
        _annotationLayer.Children.Add(bar);
        root.Children.Add(_annotationLayer);
        _annotationBoard.Dismissed += HideAnnotationLayer;
        RefreshAnnotationStatus();
    }

    Button ToolbarButton(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button
        {
            Content = text, FontSize = 11, Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(2, 0, 2, 0), MinHeight = 26
        };
        button.Click += click;
        return button;
    }

    Border ToolbarSeparator() => new()
    {
        Width = 1,
        Background = new SolidColorBrush(Color.FromRgb(58, 69, 82)),
        Margin = new Thickness(4, 2, 4, 2)
    };

    void SetAnnotationTool(AnnotationTool tool)
    {
        _annotationInk.SetTool(tool);
        RefreshAnnotationStatus();
    }

    void ChooseAnnotationColour()
    {
        var chosen = ColorPickerWindow.Pick(this, $"#{_annotationInk.InkColor.A:X2}{_annotationInk.InkColor.R:X2}{_annotationInk.InkColor.G:X2}{_annotationInk.InkColor.B:X2}");
        if (chosen is null) return;
        try
        {
            var colour = Color.Parse(chosen);
            _annotationInk.SetInk(colour, _annotationInk.InkWidth);
            if (_annotationSwatch is not null) _annotationSwatch.Background = new SolidColorBrush(colour);
            RefreshAnnotationStatus();
            SaveAnnotationPreferences();
        }
        catch { }
    }

    void ChangeAnnotationWidth(double delta)
    {
        _annotationInk.SetInk(_annotationInk.InkColor, Math.Clamp(_annotationInk.InkWidth + delta, 1, 24));
        SyncAnnotationWidthBox();
        RefreshAnnotationStatus();
        SaveAnnotationPreferences();
    }

    void CommitAnnotationWidth()
    {
        if (_annotationWidthBox is null) return;
        if (double.TryParse(_annotationWidthBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var width))
            _annotationInk.SetInk(_annotationInk.InkColor, width);
        SyncAnnotationWidthBox();
        RefreshAnnotationStatus();
        SaveAnnotationPreferences();
    }

    void SyncAnnotationWidthBox()
    {
        if (_annotationWidthBox is null) return;
        _annotationWidthBox.Text = _annotationInk.InkWidth.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    void SyncAnnotationFadePicker()
    {
        if (_annotationFadePicker is null) return;
        _settingAnnotationFade = true;
        try { _annotationFadePicker.SelectedItem = FadeLabel(_annotationBoard.Hold); }
        finally { _settingAnnotationFade = false; }
    }

    static string FadeLabel(TimeSpan hold) => hold <= TimeSpan.Zero
        ? "Permanent"
        : hold.TotalSeconds switch
        {
            <= 0.75 => "0.5 s",
            <= 1.25 => "1 s",
            <= 1.75 => "1.5 s",
            <= 2.5 => "2 s",
            <= 4 => "3 s",
            <= 7.5 => "5 s",
            _ => "10 s"
        };

    void RefreshAnnotationStatus()
    {
        if (_annotationStatus is null) return;
        _annotationStatus.Text = _annotationInk.Tool switch
        {
            AnnotationTool.Highlighter => "HIGHLIGHTER · draw across the picture",
            AnnotationTool.Eraser => "ERASER · tap a stroke",
            _ => "PEN · draw on the audience picture · Done clears"
        };
    }

    void SaveAnnotationPreferences()
    {
        try
        {
            var preferences = PreferencesStore.Load();
            preferences.AnnotationWidth = _annotationInk.InkWidth;
            preferences.AnnotationFadeSeconds = _annotationBoard.Hold <= TimeSpan.Zero
                ? 0
                : _annotationBoard.Hold.TotalSeconds;
            PreferencesStore.Save(preferences);
        }
        catch { /* the drawing toolbar must keep working even if preferences cannot be written */ }
    }

    /// <summary>The display this window shows.</summary>
    public string DisplayId => _displayId;

    /// <summary>The display's name, for the window title and the chooser.</summary>
    public string DisplayName => _catalog.FindDisplay(_displayId)?.Name ?? "Program";
    public void Publish(){_displayState=_engine.CloneState();Update();_scorebugView.Refresh();}
    /// <summary>Opens a staged layout's hand-over video before the layout is sent to air.</summary>
    public void PrepareTransition(ScorebugLayout layout) => _scorebugView.PrepareTransition(layout);
    /// <summary>Re-creates the on-air visuals; used when a new game replaces everything.</summary>
    public void Rebuild(){Update();_scorebugView.Rebuild();}
    public void UpdateLiveScoreClock(GameState source)
    {
        _displayState.HomeScore=source.HomeScore;_displayState.AwayScore=source.AwayScore;
        _displayState.HomeTeam=source.HomeTeam;_displayState.AwayTeam=source.AwayTeam;
        _displayState.HomeAbbreviation=source.HomeAbbreviation;_displayState.AwayAbbreviation=source.AwayAbbreviation;
        _displayState.ClockSeconds=source.ClockSeconds;_displayState.ClockRunning=source.ClockRunning;_displayState.ClockCountsUp=source.ClockCountsUp;
        _displayState.ShotClockSeconds=source.ShotClockSeconds;_displayState.ShotClockRunning=source.ShotClockRunning;
        _displayState.PlayClockSeconds=source.PlayClockSeconds;_displayState.PlayClockRunning=source.PlayClockRunning;
        _displayState.TimeoutClockSeconds=source.TimeoutClockSeconds;_displayState.TimeoutClockRunning=source.TimeoutClockRunning;
        // Player data goes live with the score, so a substitution prints on air immediately.
        _displayState.HomePlayerCleared=source.HomePlayerCleared;_displayState.AwayPlayerCleared=source.AwayPlayerCleared;
        _displayState.HomePlayer=source.HomePlayer;_displayState.AwayPlayer=source.AwayPlayer;
        Update();_scorebugView.Refresh();
    }
    public void RefreshDisplay(){Update();_scorebugView.Refresh();}
    /// <summary>Prints the newest values at once, used when the value switch delay changes.</summary>
    public void RefreshGraphic(){Update();_scorebugView.ApplyNow();}
    // The audience window is intentionally never restyled by ThemeManager: a theme change must not recolor on-air graphics.
    public void SetBroadcastTheme(string theme){_broadcastTheme=theme;Update();}
    void Window_Loaded(object sender, RoutedEventArgs e){Update();_scorebugView.Refresh();}
    void Update()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Update); return; }
        var s = _displayState;
        Background=ColorBrush(ThemeManager.Resolve(_broadcastTheme)=="Light"?"#FFF3F5F8":"#FF000000");
        HomeLabel.Text = s.HomeTeam; AwayLabel.Text = s.AwayTeam;
        HomeScore.Text = s.HomeScore.ToString(); AwayScore.Text = s.AwayScore.ToString();
        Period.Text = PeriodDisplay.Format(s).ToUpperInvariant(); Clock.Text = GameEngine.FormatClock(s.ClockSeconds);
        HomeExtraLabel.Text = AwayExtraLabel.Text = "TIMEOUTS";
        HomeExtraPanel.Visibility = AwayExtraPanel.Visibility = Visibility.Visible;
        HomeTimeouts.Text = Dots(s.HomeTimeouts); AwayTimeouts.Text = Dots(s.AwayTimeouts);
        Possession.Visibility = s.SportId is "basketball" or "football" ? Visibility.Visible : Visibility.Collapsed;
        Possession.Text = $"●  {(s.HomePossession ? s.HomeTeam : s.AwayTeam)} POSSESSION";
        if(s.SportId=="football")
        {
            Period.Text=$"{PeriodDisplay.Format(s).ToUpperInvariant()} · {s.Down}{OrdinalSuffix(s.Down)} & {s.YardsToGo}";
            Possession.Text=$"●  {(s.HomePossession?s.HomeTeam:s.AwayTeam)} POSSESSION   ·   PLAY CLOCK {s.PlayClockSeconds}   ·   TIMEOUT CLOCK {GameEngine.FormatClock(s.TimeoutClockSeconds)}";
        }
        if(s.SportId=="soccer")
        {
            HomeExtraLabel.Text = AwayExtraLabel.Text = "CARDS · SHOTS · CORNERS";
            HomeTimeouts.Text=$"🟨 {s.HomeYellowCards}   🟥 {s.HomeRedCards}   ⚽ {s.HomeShots}   ◆ {s.HomeCorners}";
            AwayTimeouts.Text=$"🟨 {s.AwayYellowCards}   🟥 {s.AwayRedCards}   ⚽ {s.AwayShots}   ◆ {s.AwayCorners}";
            Possession.Visibility=Visibility.Collapsed;
        }
        else if(s.SportId=="basketball")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="FOULS";HomeTimeouts.Text=$"{s.HomeFouls} fouls";AwayTimeouts.Text=$"{s.AwayFouls} fouls";
            Possession.Text=$"●  {(s.HomePossession?s.HomeTeam:s.AwayTeam)} POSSESSION   ·   SHOT CLOCK {s.ShotClockSeconds}";
        }
        else if(s.SportId=="baseball")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="HITS · COUNT";HomeTimeouts.Text=$"{s.HomeHits} hits   ·   {s.Balls} balls / {s.Strikes} strikes";AwayTimeouts.Text=$"{s.AwayHits} hits   ·   {s.Outs} outs";Possession.Visibility=Visibility.Collapsed;
        }
        else if(s.SportId=="football")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="YARDS · TURNOVERS";HomeTimeouts.Text=$"{s.HomeYards} yards   ·   {s.HomeTurnovers} turnovers";AwayTimeouts.Text=$"{s.AwayYards} yards   ·   {s.AwayTurnovers} turnovers";
        }
    }
    static Avalonia.Media.Brush ColorBrush(string value)=>new SolidColorBrush(Color.Parse(value));
    static string Dots(int count) => string.Join(" ", Enumerable.Repeat("●", count));
    static string OrdinalSuffix(int value)=>value switch{1=>"st",2=>"nd",3=>"rd",_=>"th"};
    /// <summary>True while this window is filling its screen.</summary>
    public bool IsFullscreen => _full;

    public void ToggleFullscreen()
    {
        if (_full) ExitFullscreen();
        else EnterFullscreen();
    }

    void EnterFullscreen()
    {
        _full = true;
        _style = SystemDecorations;
        _state = WindowState;
        _left = Left;
        _top = Top;
        _width = Width;
        _height = Height;
        _geometrySaved = true;
        // Drop back to a normal window before the chrome goes: maximizing while the style is being
        // changed makes WPF size the window to the old work area, which is what left a strip down
        // each side of a 16:9 graphic (the "black borders" in fullscreen).
        WindowState = WindowState.Normal;
        SystemDecorations = SystemDecorations.None;
        ResizeMode = ResizeMode.NoResize;
        FillMonitor();
        Topmost = true;
    }

    void ExitFullscreen()
    {
        _full = false;
        Topmost = false;
        SystemDecorations = _style;
        ResizeMode = ResizeMode.CanResize;
        if (_geometrySaved)
        {
            Left = _left;
            Top = _top;
            Width = _width;
            Height = _height;
            _geometrySaved = false;
        }
        WindowState = _state;
    }

    /// <summary>Takes the window to the full bounds of the monitor it is on, taskbar included.</summary>
    void FillMonitor()
    {
        try
        {
            // The screen backend already knows the monitor geometry: xrandr under X11, the portal
            // under Wayland. Divide its physical bounds by that monitor's scale, just as ScreenFit
            // does for ordinary windows; no win32 handle is involved.
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            var scale = screen.Scaling > 0 ? screen.Scaling : RenderScaling;
            if (scale <= 0) scale = 1;
            Left = screen.Bounds.X / scale;
            Top = screen.Bounds.Y / scale;
            Width = screen.Bounds.Width / scale;
            Height = screen.Bounds.Height / scale;
        }
        catch (Exception) { }
    }

    /// <summary>
    /// F11 on the audience window is the same as Broadcast Fullscreen from the main menu: the
    /// graphic fills the screen. Escape gives the window back.
    /// </summary>
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (e.Key == Avalonia.Input.Key.T)
        {
            if (AnnotationToolsOpen) CloseAnnotationTools(); else OpenAnnotationTools();
            e.Handled = true;
            return;
        }
        if (AnnotationToolsOpen)
        {
            if (e.Key == Avalonia.Input.Key.P) { SetAnnotationTool(AnnotationTool.Pen); e.Handled = true; return; }
            if (e.Key == Avalonia.Input.Key.H) { SetAnnotationTool(AnnotationTool.Highlighter); e.Handled = true; return; }
            if (e.Key == Avalonia.Input.Key.E) { SetAnnotationTool(AnnotationTool.Eraser); e.Handled = true; return; }
            if (e.Key == Avalonia.Input.Key.Z && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) { _annotationInk.UndoInk(); e.Handled = true; return; }
            if (e.Key == Avalonia.Input.Key.Delete) { _annotationInk.ClearInk(); e.Handled = true; return; }
        }
        if (e.Key == Avalonia.Input.Key.Escape && AnnotationToolsOpen)
        {
            CloseAnnotationTools();
            e.Handled = true;
            return;
        }
        if (e.Key == Avalonia.Input.Key.Escape && _full)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
    protected override void OnClosed(EventArgs e) { _engine.Changed-=Update; _engine.Changed-=_scorebugView.Refresh; _catalog.Changed-=Publish; _annotationBoard.Dismissed -= HideAnnotationLayer; _annotationBoard.EndStroke(); _scorebugView.Dispose(); base.OnClosed(e); }
}
