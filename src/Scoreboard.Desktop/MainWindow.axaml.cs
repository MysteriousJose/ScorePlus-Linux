using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Win32;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public partial class MainWindow : Window, IControlPanelHost, IRemoteDisplayHost
{
    readonly GameEngine _engine = new();
    readonly GameEngine _publishedEngine = new();
    readonly ScorebugCatalog _scorebug = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(5) };
    // The rail's peak meter, redrawn a few times a second; the level itself is shared by every window.
    readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    readonly Border[] _meterBars = new Border[22];
    int _meterLit = -1;
    readonly List<GameControlWindow> _controlPanels = [];
    readonly ControlPanelView _panel;
    // This window's own display choice and staged layouts. Another panel can stage a different
    // display at the same time; only Update Output sends anything to an audience.
    readonly ProgramSelection _program;
    readonly MediaSession _session;
    readonly ReplayBoothController _booth;
    readonly HardwareService _hardware;
    readonly AutomationRuntime _automation;
    QuickMediaBar? _quickMedia;
    AutomationWindow? _automationWindow;
    IconMakerWindow? _iconMaker;
    bool _operatorLocked;
    bool _workspaceEnabled = true;
    bool _displaySync;
    readonly MediaPlayback _playback;
    // Every audience window ever opened this session. Opening the output again adds another one
    // instead of focusing the old window, so a show can spread the graphic over several displays.
    readonly List<OutputWindow> _outputs = [];
    // One ink board per display, shared by every audience window showing it.
    readonly Dictionary<string, AnnotationBoard> _annotationBoards = new(StringComparer.OrdinalIgnoreCase);
    // What /watch is showing, one frame per display, and a counter the page watches so a wall
    // screen only fetches a picture when the graphic changed.
    readonly Dictionary<string, (string Signature, byte[]? Png, long Version)> _watchFrames = new(StringComparer.Ordinal);
    long _watchVersion;
    // Automatic horn edge detection: arm only while a clock was actually running, so loading a
    // game at 0:00 or typing 0 into a stopped clock stays silent.
    int _lastGameClock = -1;
    int _lastPlayClock = -1;
    bool _lastGameClockRunning;
    bool _lastPlayClockRunning;
    bool _autoHornReady;
    EventLogWindow? _log;
    RemoteControlServer? _remote;
    ScorebugCanvasView? _preview;
    AudiencePreview? _audience;
    // The four assignable quick-layout buttons above the rail's meter.
    QuickLayoutBar? _quickLayouts;
    MacroDock? _macroDock;
    LeagueCloudService? _leagueCloud;
    int _eventCount = -1;
    readonly UserPreferences _preferences = PreferencesStore.Load();
    string? _projectPath;
    bool _initializing = true;
    bool _liveScoreClock = true;
    // Set while the sport picker is being filled in for the operator, so syncing the control cannot
    // be mistaken for the operator choosing a sport (which starts a new game).
    bool _sportSync;
    // Set while the game-name box is being filled from the engine, so that does not count as typing.
    bool _gameNameSync;
    // The next autosave starts a fresh recoverable point instead of updating the newest one. Set
    // when a new game starts or a game is loaded, so the previous game keeps its own autosave.
    bool _startNewAutosavePoint = true;
    // The game state and the design as they were the last time they went into a .spg file (or
    // started fresh), so a changed layout counts as an unsaved change too.
    string? _savedStateJson;
    string? _savedDesignJson;
    bool _closingAfterPrompt;
    GameControlWindow.ConsoleTab _tab = GameControlWindow.ConsoleTab.Control;
    // How the shell is laid out right now: full, compact (narrow headers, slim rail) or the
    // overlay tier where the rail hides behind a header toggle instead of squeezing the console.
    enum RailTier { Wide, Compact, Overlay }
    RailTier _railTier;
    bool _narrow;
    bool _railOverlayOpen;
    // The rail width the operator last had at full size, restored when the window grows back.
    double _railPreferredWidth = 336;
    /// <summary>Below this content width the headers compact and the rail narrows.</summary>
    const double NarrowWidth = 980;
    /// <summary>Below this content width the rail would squeeze the console, so it becomes an overlay.</summary>
    const double RailOverlayWidth = 860;
    /// <summary>Below this content height the workspace scrolls instead of clipping its bottom.</summary>
    const double ScrollBelowHeight = 900;

    /// <summary>
    /// The exit prompt is on for operators. The UI checks turn it off because they create and close
    /// windows deliberately, and a modal prompt would stall an unattended run.
    /// </summary>
    public static bool PromptOnExit { get; set; } = true;

    /// <summary>
    /// Warns before a sport switch clears the old sport's values. The checks turn it off because they
    /// change sports while driving the console, and a modal warning would stall an unattended run.
    /// </summary>
    public static bool WarnOnSportSwitch { get; set; } = true;

    /// <summary>Lets an unattended check start a window without reaching the update feed.</summary>
    public static bool CheckForUpdatesOnLoad { get; set; } = true;

    /// <param name="mode">How the app was started: <see cref="LaunchMode.New"/> skips the autosave,
    /// <see cref="LaunchMode.Open"/> loads <paramref name="gamePath"/>. Null keeps the legacy
    /// behaviour of restoring the last autosave, which the tests rely on.</param>
    /// <param name="waitForLaunch">True when the launcher box opens on top of this window: no game
    /// is started or restored until the operator chooses one, and the workspace stays locked.</param>
    public MainWindow(LaunchMode? mode = null, string? gamePath = null, bool waitForLaunch = false)
    {
        InitializeComponent();
        // Follow the window onto portrait monitors and laptops at 150% scaling instead of hanging
        // the console off the screen; the console itself is reflowable down to its minimum width.
        ScreenFit.Track(this);
        // Reflow the shell as the window changes size. The handlers are names-only; only the
        // widths of the header strips and the rail column change, never which control a button
        // is or what it does.
        SizeChanged += (_, e) => ApplyShellLayout(e.NewSize.Width, e.NewSize.Height);
        _session = new MediaSession(_preferences);
        _booth = new ReplayBoothController(_session, _engine, _preferences);
        _hardware = new HardwareService(_engine);
        _hardware.Snapshot += snapshot => Dispatcher.BeginInvoke(() => ApplyHardware(snapshot));
        _hardware.Status += message => Dispatcher.BeginInvoke(() => StatusText.Text = message);
        _automation = new AutomationRuntime(_scorebug, _publishedEngine, _engine);
        _playback = _session.Playback;
        ThemeManager.Apply(this, _preferences.Theme);
        BuildSoundMeter();
        _meterTimer.Tick += (_, _) => UpdateSoundMeter();
        _meterTimer.Start();
        DisplayTiming.Apply(_preferences);
        _quickMedia = new QuickMediaBar(_session, _preferences.Theme);
        _quickMedia.Status += message => StatusText.Text = message;
        QuickMediaHost.Content = _quickMedia;
        _quickLayouts = new QuickLayoutBar(_scorebug, _engine, _preferences.Theme);
        _quickLayouts.Status += message => StatusText.Text = message;
        QuickLayoutsHost.Content = _quickLayouts;
        SportSelector.SelectedValuePath = "Id";
        RefreshSports();
        SportSelector.SelectedValue = _engine.State.SportId;
        // Arm the rules the console reads before the first down is pressed.
        GameEngine.DownPreviewEnabled = _preferences.NextDownPreview;
        GameEngine.NewSeriesLiveEnabled = _preferences.NewSeriesLive;

        _engine.Changed += Refresh;
        _engine.Changed += EngineChanged;
        _engine.Changed += () => ReplayBoothHistory.Sample(_engine.State);
        _leagueCloud = new LeagueCloudService(_engine, _preferences);
        _leagueCloud.Status += message => Dispatcher.BeginInvoke(new Action(() => StatusText.Text = message));
        _engine.Changed += () => _leagueCloud?.MarkDirty();
        _timer.Tick += (_, _) => _engine.Tick();
        _timer.Start();
        _autosave.Tick += (_, _) => AutoSave();
        _autosave.Start();

        _program = new ProgramSelection(_scorebug);
        _program.Changed += ProgramChanged;
        _preview = new ScorebugCanvasView(_scorebug, _publishedEngine, playback: _playback, layoutProvider: () => _program.Layout, displayId: _program.DisplayId, automation: _automation);
        PreviewHost.Content = _preview;
        // The monitor reads the published engine and the published layout, so it is a real
        // reflection of the audience window rather than another staged preview.
        _audience = new AudiencePreview(_scorebug, _publishedEngine, _playback, _program.DisplayId);
        AudienceHost.Content = _audience;
        SyncDisplayPicker();
        RefreshLayoutIndicators();
        ShowTab(GameControlWindow.ConsoleTab.Control);
        // The theme pass repaints every button when the window loads, which used to wipe the green
        // outline off the already-selected tab until another tab was clicked.
        Loaded += (_, _) =>
        {
            ApplyTabVisuals();
            // The remote and the update check belong to a live window, not to the launch sequence.
            if (!waitForLaunch) { StartRemote(); StartHardware(); _leagueCloud?.Start(); _ = CheckForUpdatesAsync(); }
        };

        // Exit prompt: the world has no OnClosing override; Avalonia's Closing event carries the same
        // CancelEventArgs-style cancel. Set cancel, prompt, then re-post Close on the UI thread.
        Closing += (_, e) =>
        {
            if (PromptOnExit && !_closingAfterPrompt && HasUnsavedChanges())
            {
                e.Cancel = true;
                var detail = "You have changes that are not in a saved game file. Autosave keeps a recovery point every few seconds, but the .spg on disk is out of date. Save it before closing?";
                var choice = UnsavedChangesWindow.Ask(this, _preferences.Theme, detail);
                if (choice == UnsavedChoice.Cancel) return;
                if (choice == UnsavedChoice.Save && !SaveBeforeClosing()) return;
                _closingAfterPrompt = true;
                // WPF forbids Close/ShowDialog while the closing event is still running. Re-post the
                // close once this cancellation has fully finished, so Discard really exits.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
                return;
            }
        };
        // Keyboard preview: no OnPreviewKeyDown override in the world. Avalonia has no separate
        // preview-key event, but KeyDownEvent is registered Tunnel|Bubble, so a tunnel handler on the
        // window runs on the way down — the same moment WPF's PreviewKeyDown fires. The world reports
        // the real key directly (no Key.System indirection) and carries the modifiers on the args.
        AddHandler(InputElement.KeyDownEvent, new EventHandler<KeyEventArgs>((_, e) =>
        {
            var key = e.Key;
            // Enter settles whatever is being typed and leaves the window open. There is no default
            // button on this window, so without this an Enter in a field would do nothing at all.
            if (key == Key.Enter && Keyboard.FocusedElement is TextBox box && !box.AcceptsReturn)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
                return;
            }
            if (_panel?.TryHandleKey(key, e.KeyModifiers) == true) { e.Handled = true; return; }
        }), RoutingStrategies.Tunnel);

        _panel = new ControlPanelView(_engine, this);
        ControlHost.Content = _panel;
        _panel.SetHintVisibility(_preferences.ShowHeadingHints);
        _macroDock = new MacroDock(_engine, _session, _preferences.Theme, _panel.Execute, _scorebug, PublishGameState);
        _macroDock.Status += message => StatusText.Text = message;
        MacroHost.Content = _macroDock;

        RosterControl.Attach(_engine);
        RosterControl.ApplyTheme(_preferences.Theme);
        RosterControl.SetHintVisibility(_preferences.ShowHeadingHints);
        MediaControl.Attach(_session);
        MediaControl.ApplyTheme(_preferences.Theme);
        // The dock now lives in a full-width tab instead of a narrow rail, so its controls are the
        // same size as the console's.
        MediaControl.SetLargeMode(true);
        _session.Status += message => StatusText.Text = message;
        ApplyHintVisibility();

        Refresh();
        RefreshEndGameButton();
        _initializing = false;

        if (waitForLaunch)
        {
            // The main window is already up behind the launcher, with nothing to change yet.
            _savedStateJson = _engine.ExportJson();
            _savedDesignJson = DesignJson();
            SetWorkspaceEnabled(false);
            StatusText.Text = "WAITING · choose New Game or Open Game in the launch window";
            return;
        }

        // No recovery prompt: a "New Game" launch always starts clean, "Open Game" loads the chosen
        // file, and the window shows whatever the newest autosave holds only when no choice was made.
        if (mode == LaunchMode.New)
        {
            StartNewGame(string.IsNullOrWhiteSpace(_preferences.LastSport) ? "football" : _preferences.LastSport, announce: false);
            StatusText.Text = "NEW GAME · set the teams and you are live";
        }
        else if (mode == LaunchMode.Open && !string.IsNullOrWhiteSpace(gamePath))
        {
            LoadGame(gamePath);
        }
        else if (mode is null)
        {
            AutosaveStore.ImportLegacy();
            var newest = AutosaveStore.List().FirstOrDefault();
            if (newest is not null && LoadGame(newest.Path))
                StatusText.Text = $"AUTOSAVE RESTORED · {newest.WhenLabel} (New Game starts fresh)";
        }

        _savedStateJson ??= _engine.ExportJson();
        _savedDesignJson ??= DesignJson();

        // Panels are opened deliberately with New Panel; starting a game never spawns a second window.
    }

    /// <summary>
    /// Applies the choice made in the launch box to the window that is already open behind it.
    /// </summary>
    public void StartFromLauncher(LaunchMode mode, string? path, string? sportId = null)
    {
        if (mode == LaunchMode.Open && !string.IsNullOrWhiteSpace(path) && LoadGame(path))
            StatusText.Text = $"GAME OPENED · {Path.GetFileNameWithoutExtension(path)}";
        else
        {
            StartNewGame(sportId ?? (string.IsNullOrWhiteSpace(_preferences.LastSport) ? "football" : _preferences.LastSport), announce: false);
            StatusText.Text = "NEW GAME · set the teams and you are live";
        }
        _savedStateJson = _engine.ExportJson();
        _savedDesignJson = DesignJson();
        SetWorkspaceEnabled(true);
        Activate();
        StartRemote();
        StartHardware();
        _leagueCloud?.Start();
        _ = CheckForUpdatesAsync();
    }

    /// <summary>
    /// Fits the shell to the window: the two header strips wrap and compact, the rail narrows and
    /// then becomes an overlay, and a window too short for the console scrolls the workspace.
    /// Idempotent, so it can run on every resize without disturbing the operator's splitter width.
    /// </summary>
    void ApplyShellLayout(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0) return;

        var narrow = width < NarrowWidth;
        if (narrow != _narrow)
        {
            _narrow = narrow;
            Root.Classes.Set("narrow", narrow);
            // Give the action rows back the room the fields were holding, so the buttons wrap
            // less instead of growing the header downward.
            GameNameBox.Width = narrow ? 110 : 150;
            SportSelector.MaxWidth = narrow ? 160 : 220;
            // At the compact tier the header hints are the first thing to give up their room.
            ApplyHintVisibility();
        }

        // A narrow window trims the media desk's own chrome too, so its columns keep the room.
        MediaControl.SetCompact(narrow);

        SetRailTier(width < RailOverlayWidth ? RailTier.Overlay
            : narrow ? RailTier.Compact
            : RailTier.Wide);
        // The overlay is a panel over the console, so it is never taller than the visible
        // workspace; its own scroller takes over when the rail's cards do not fit.
        if (_railTier == RailTier.Overlay) RailHost.MaxHeight = Math.Max(220, WorkspaceViewportHeight());

        // A scroller measures its content with unlimited height, which turns the console's star
        // rows into auto rows. Only switch it on when the window is too short to hold the console;
        // at normal heights the rows must keep filling the window as they always have.
        var scrolling = double.IsFinite(height) && height > 0 && height < ScrollBelowHeight;
        var mode = scrolling
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        if (WorkspaceScroller.VerticalScrollBarVisibility != mode)
            WorkspaceScroller.VerticalScrollBarVisibility = mode;
    }

    /// <summary>
    /// Moves the rail between its three tiers. The operator's own splitter width is remembered at
    /// full size and put back when the window grows again; the compact and overlay tiers force
    /// their widths so the console keeps working.
    /// </summary>
    void SetRailTier(RailTier tier)
    {
        if (tier == _railTier) return;
        var column = Workspace.ColumnDefinitions[0];
        if (_railTier == RailTier.Wide && double.IsFinite(column.Width.Value) && column.Width.Value > 0)
            _railPreferredWidth = Math.Max(240, column.Width.Value);
        _railTier = tier;
        switch (tier)
        {
            case RailTier.Wide:
                column.MinWidth = 260;
                column.Width = new GridLength(_railPreferredWidth);
                break;
            case RailTier.Compact:
                column.MinWidth = 240;
                column.Width = new GridLength(240);
                break;
            default:
                column.MinWidth = 0;
                column.Width = new GridLength(0);
                break;
        }

        var overlay = tier == RailTier.Overlay;
        if (overlay) _railOverlayOpen = false;
        RailToggleButton.IsVisible = overlay;
        RailSplitter.IsVisible = !overlay;
        RailHost.IsVisible = !overlay || _railOverlayOpen;
        // An overlay rail spans every column and sits above the console, so nothing is removed
        // from the operator's reach; the toggle in the header brings it back and puts it away.
        Grid.SetColumnSpan(RailHost, overlay ? 3 : 1);
        RailHost.HorizontalAlignment = overlay ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        RailHost.Width = overlay ? RailWidthForWindow() : double.NaN;
        RailHost.Margin = overlay ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        RailHost.ZIndex = overlay ? 10 : 0;
        RailHost.MaxHeight = overlay ? Math.Max(220, WorkspaceViewportHeight()) : double.PositiveInfinity;
    }

    double WorkspaceViewportHeight()
    {
        var viewport = WorkspaceScroller.Bounds.Height;
        if (!double.IsFinite(viewport) || viewport <= 0) viewport = Math.Max(0, Bounds.Height - 150);
        return viewport;
    }

    double RailWidthForWindow()
    {
        var available = Root.Bounds.Width - 24;
        return double.IsFinite(available) && available > 0 ? Math.Clamp(available, 220, 320) : 320;
    }

    void RailToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_railTier != RailTier.Overlay) return;
        _railOverlayOpen = !_railOverlayOpen;
        RailHost.IsVisible = _railOverlayOpen;
        RailHost.Width = RailWidthForWindow();
        RailHost.MaxHeight = Math.Max(220, WorkspaceViewportHeight());
        RailToggleButton.Content = _railOverlayOpen ? "◨" : "◧";
    }

    /// <summary>Locks or unlocks everything inside the window while the launch box is up.</summary>
    void SetWorkspaceEnabled(bool enabled)
    {
        _workspaceEnabled = enabled;
        Root.IsEnabled = enabled && !_operatorLocked;
        LockButton.IsEnabled = enabled && !_operatorLocked;
        // A locked window reads as dimmed, so nobody tries to type into a game that is not there.
        Root.Opacity = _operatorLocked ? 0.35 : enabled ? 1 : 0.55;
    }

    /// <summary>The live game, exposed so the operator surface (and the checks) can drive it.</summary>
    public GameEngine Engine => _engine;

    /// <summary>The copy currently on air (preview and audience output).</summary>
    public GameState PublishedState => _publishedEngine.State;

    /// <summary>The console embedded in this window, exposed for the checks.</summary>
    public ControlPanelView Panel => _panel;

    /// <summary>The monitor that mirrors what the audience is watching, exposed for the checks.</summary>
    public AudiencePreview? AudienceMonitor => _audience;

    /// <summary>The scorebug layouts and design, exposed so the checks can drive the layout arrows.</summary>
    public ScorebugCatalog Scorebug => _scorebug;

    // ---------- IControlPanelHost ----------
    public bool LiveScoreClock
    {
        get => _liveScoreClock;
        set => SetLiveScoreClock(value);
    }

    public bool HasStagedChanges =>
        GameEngine.StagedSignature(_engine.State) != GameEngine.StagedSignature(_publishedEngine.State) ||
        _program.HasStagedChanges;

    /// <summary>The layout the preview is showing: the one that Update Output will publish.</summary>
    public string LayoutLabel => _program.Label;

    public void OpenNewPanel() => OpenControlPanel();

    // The phone can preview any display and move that display's staged layout without taking the
    // desktop preview away from whatever the operator is working on.
    public IReadOnlyList<(string Id, string Name)> RemoteDisplays =>
        _scorebug.Displays.Select(display => (display.Id, display.Name)).ToArray();

    public string RemoteDisplayId => _program.DisplayId;

    public string RemoteLayoutLabel(string displayId) => _program.LabelFor(displayId);

    public void RemoteSelectDisplay(string displayId)
    {
        if (string.IsNullOrWhiteSpace(displayId)) return;
        _program.SelectDisplay(displayId);
        _preview?.Rebuild();
        SyncDisplayPicker();
        RefreshLayoutIndicators();
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
    }

    public void RemoteShiftLayout(string displayId, int delta)
    {
        var target = string.IsNullOrWhiteSpace(displayId) ? _program.DisplayId : displayId;
        _program.ShiftFor(target, delta);
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        var display = _scorebug.FindDisplay(target);
        StatusText.Text = $"REMOTE LAYOUT · {display?.Name ?? "Display"} · {_program.LabelFor(target)} · preview only";
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
    }

    /// <summary>
    /// Sends the game data (score, clocks, down, stats) to the audience copies. It deliberately
    /// touches no layout, so a detached panel can publish its own display right after it.
    /// </summary>
    public void PublishGameState()
    {
        _publishedEngine.LoadFromState(_engine.CloneState());
        _preview?.Refresh();
        // The monitor listens to the published engine itself, so the copy above already repaints it.
        foreach (var output in _outputs) output.Publish();
    }

    public void PublishOutput()
    {
        // Arm the hand-over while the operator is still on the preview, so the first frame is
        // decoded before the switch instead of starting with a black gap.
        PrepareTransitionForStagedLayout();
        // The chosen layouts go on air with the game data: the preview and output still read
        // different copies, so the switch only happens here.
        PublishGameState();
        _program.PublishStaged();
        RefreshLayoutIndicators();
        StatusText.Text = $"OUTPUT UPDATED · {LayoutLabel.ToUpperInvariant()} · {DateTime.Now:HH:mm:ss}";
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
    }

    /// <summary>Moves the previewed layout; nothing goes on air until Update Output is pressed.</summary>
    public void ShiftLayout(int delta)
    {
        _program.Shift(delta);
        PrepareTransitionForStagedLayout();
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        StatusText.Text = _program.HasStagedChanges
            ? $"LAYOUT · {LayoutLabel} · preview only — press Update Output"
            : $"LAYOUT · {LayoutLabel} · ON AIR";
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
    }

    void ProgramChanged()
    {
        SyncDisplayPicker();
        RefreshLayoutIndicators();
        PrepareTransitionForStagedLayout();
        _preview?.Rebuild();
    }

    /// <summary>
    /// Arms the staged layout's hand-over video on the preview and every visible output, so a
    /// layout with a transition clip starts on its first frame instead of a black gap.
    /// </summary>
    void PrepareTransitionForStagedLayout()
    {
        _preview?.PrepareTransition(_program.Layout);
        foreach (var output in _outputs.Where(output => output.IsVisible))
            if (_program.LayoutFor(output.DisplayId) is { } layout)
                output.PrepareTransition(layout);
    }

    /// <summary>Fills the display picker without treating the sync as the operator's choice.</summary>
    void SyncDisplayPicker()
    {
        _displaySync = true;
        try
        {
            var names = _scorebug.Displays.Select(display => display.Name).ToArray();
            if (DisplaySelector.ItemsSource is not IEnumerable<string> existing || !existing.SequenceEqual(names))
                DisplaySelector.ItemsSource = names;
            var index = _scorebug.Displays.FindIndex(display => string.Equals(display.Id, _program.DisplayId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && DisplaySelector.SelectedIndex != index) DisplaySelector.SelectedIndex = index;
        }
        finally { _displaySync = false; }
    }

    void Display_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_displaySync || DisplaySelector.SelectedIndex < 0 || DisplaySelector.SelectedIndex >= _scorebug.Displays.Count) return;
        _program.SelectDisplay(_scorebug.Displays[DisplaySelector.SelectedIndex].Id);
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        StatusText.Text = $"PREVIEW · {_program.Display.Name} · {LayoutLabel}";
    }

    void RefreshLayoutIndicators()
    {
        PreviewLayoutLabel.Text = LayoutLabel;
        var count = _program.Display.Layouts.Count;
        LayoutPrevButton.IsEnabled = LayoutNextButton.IsEnabled = count > 1;
    }

    // ---------- tabs ----------

    /// <summary>Switches the operator surface between the console and the media desk.</summary>
    public void ShowMediaTab(bool media) => ShowTab(media ? GameControlWindow.ConsoleTab.Media : GameControlWindow.ConsoleTab.Control);

    void ControlTab_Click(object sender, RoutedEventArgs e) => ShowTab(GameControlWindow.ConsoleTab.Control);

    void MediaTab_Click(object sender, RoutedEventArgs e) => ShowTab(GameControlWindow.ConsoleTab.Media);

    void RosterTab_Click(object sender, RoutedEventArgs e) => ShowTab(GameControlWindow.ConsoleTab.Rosters);

    void MacrosTab_Click(object sender, RoutedEventArgs e) => ShowTab(GameControlWindow.ConsoleTab.Macros);

    /// <summary>Switches the operator surface between the console, media, roster and macro desks.</summary>
    void ShowTab(GameControlWindow.ConsoleTab tab)
    {
        if (_tab == GameControlWindow.ConsoleTab.Macros && tab != GameControlWindow.ConsoleTab.Macros)
            _macroDock?.CommitPendingEdits();
        _tab = tab;
        ControlHost.Visibility = tab == GameControlWindow.ConsoleTab.Control ? Visibility.Visible : Visibility.Collapsed;
        MediaControl.Visibility = tab == GameControlWindow.ConsoleTab.Media ? Visibility.Visible : Visibility.Collapsed;
        RosterControl.Visibility = tab == GameControlWindow.ConsoleTab.Rosters ? Visibility.Visible : Visibility.Collapsed;
        MacroHost.Visibility = tab == GameControlWindow.ConsoleTab.Macros ? Visibility.Visible : Visibility.Collapsed;
        ApplyTabVisuals();
        if (tab == GameControlWindow.ConsoleTab.Media) MediaControl.RefreshView();
        if (tab == GameControlWindow.ConsoleTab.Rosters) RosterControl.RefreshView();
        if (tab == GameControlWindow.ConsoleTab.Macros) _macroDock?.RefreshView();
    }

    /// <summary>
    /// Paints the active tab. Split out because the theme pass repaints every button when the
    /// window loads, which silently erased the green outline on the tab that was already selected.
    /// </summary>
    void ApplyTabVisuals()
    {
        // Active-tab colours follow the operator's accent from Preferences.
        var accent = UiPalette.Accent(_preferences);
        var on = UiPalette.Frozen(UiPalette.Soft(accent));
        var off = new SolidColorBrush(Color.Parse("#141A23"));
        var onBorder = UiPalette.Frozen(accent);
        var offBorder = new SolidColorBrush(Color.Parse("#28323F"));
        var muted = new SolidColorBrush(Color.Parse("#93A0B2"));
        Button[] tabs = [ControlTab, MediaTab, RosterTab, MacrosTab];
        var active = (int)_tab;
        for (var i = 0; i < tabs.Length; i++)
        {
            tabs[i].Background = i == active ? on : off;
            tabs[i].BorderBrush = i == active ? onBorder : offBorder;
            tabs[i].Foreground = i == active ? Brushes.White : muted;
        }
    }

    void LayoutPrev_Click(object sender, RoutedEventArgs e) => ShiftLayout(-1);

    void LayoutNext_Click(object sender, RoutedEventArgs e) => ShiftLayout(1);

    // ---------- window chrome ----------
    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var s = _engine.State;
        if (SportSelector.SelectedValue is not string selectedId || selectedId != s.SportId)
        {
            _sportSync = true;
            try { SportSelector.SelectedValue = s.SportId; }
            finally { _sportSync = false; }
        }
        // Follow a new or loaded game without overwriting a name the operator is currently typing.
        if (GameNameBox is not null && !GameNameBox.IsKeyboardFocusWithin && GameNameBox.Text != s.GameName)
        {
            _gameNameSync = true;
            try { GameNameBox.Text = s.GameName; }
            finally { _gameNameSync = false; }
        }
        _eventCount = s.Events.Count;
    }

    void RefreshSports() => SportSelector.ItemsSource = _engine.Sports.All;

    /// <summary>
    /// Builds the rail's peak meter once: a column of segments whose fills the tick only repaints.
    /// Green up to a healthy level, amber near the top, red at full scale.
    /// </summary>
    void BuildSoundMeter()
    {
        var unlit = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30));
        unlit.Freeze();
        for (var index = 0; index < _meterBars.Length; index++)
        {
            var fraction = (index + 1) / (double)_meterBars.Length;
            var color = fraction > 0.92 ? Color.FromRgb(0xFF, 0x6B, 0x6B)
                : fraction > 0.78 ? Color.FromRgb(0xE8, 0xB2, 0x3A)
                : Color.FromRgb(0x38, 0xD7, 0xA3);
            var bar = new Border
            {
                Background = unlit,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(1, 0, 1, 0),
                ToolTip = fraction > 0.92 ? "Full scale — the output is clipping" : null
            };
            bar.Tag = color;
            ThemeManager.SetSkipTheme(bar, true);
            _meterBars[index] = bar;
            SoundMeterSegments.Children.Add(bar);
        }
    }

    /// <summary>
    /// Repaints the meter and the show line beside it: what is on air, and the score and clock the
    /// audience is actually looking at. Read from the published copy, not from staged edits.
    /// </summary>
    void UpdateSoundMeter()
    {
        if (_meterBars[0] is null) return;
        var level = SoundLevelMeter.Read();
        // A square-root scale so quiet speech still moves the bar, the way a real meter does.
        var lit = (int)Math.Round(Math.Sqrt(level) * _meterBars.Length);
        if (lit != _meterLit)
        {
            _meterLit = lit;
            var unlit = new SolidColorBrush(Color.Parse("#1B2430"));
            for (var index = 0; index < _meterBars.Length; index++)
                _meterBars[index].Background = index < lit && _meterBars[index].Tag is Color barColor
                    ? new SolidColorBrush(barColor)
                    : unlit;
        }
        SoundLevelText.Text = level <= 0
            ? (SoundLevelMeter.Available ? "−∞ dB" : "no output")
            : $"{SoundLevelMeter.Decibels(level):0} dB";
        if (_preview is null) { OnAirInfo.Text = "STANDBY"; ShowInfo.Text = "Start a game to go live"; return; }
        OnAirInfo.Text = _program.HasStagedChanges ? "STAGED · UPDATE TO AIR" : $"ON AIR · {_program.Display.Name.ToUpperInvariant()}";
        var s = _publishedEngine.State;
        ShowInfo.Text = $"{s.AwayAbbreviation} {s.AwayScore} · {s.HomeAbbreviation} {s.HomeScore} · {GameEngine.FormatClock(s.ClockSeconds)} · {PeriodDisplay.Format(s)}";
    }

    void ApplyHintVisibility()
    {
        // Hints are the first thing the compact tier gives up, whatever the preference says.
        var visibility = _preferences.ShowHeadingHints && !_narrow ? Visibility.Visible : Visibility.Collapsed;
        SportHint.Visibility = PreviewAudienceHint.Visibility = visibility;
        MediaControl.SetHintVisibility(_preferences.ShowHeadingHints);
        RosterControl.SetHintVisibility(_preferences.ShowHeadingHints);
        _macroDock?.SetHintVisibility(_preferences.ShowHeadingHints);
        _log?.SetHintVisibility(_preferences.ShowHeadingHints);
    }

    void AdLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AdLogWindow(_preferences.Theme) { Owner = this };
        dialog.ShowDialog();
    }

    void Hardware_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HardwareWindow(_preferences, _hardware) { Owner = this };
        dialog.ShowDialog();
    }

    void Automation_Click(object sender, RoutedEventArgs e)
    {
        if (_automationWindow is { IsVisible: true }) { _automationWindow.Activate(); return; }
        var dialog = new AutomationWindow(_scorebug, _publishedEngine, _preferences.Theme,
            _program.DisplayId, _automation, _engine, layoutId: _program.Layout.Id) { Owner = this };
        _automationWindow = dialog;
        dialog.Closed += (_, _) => { _automationWindow = null; _preview?.Rebuild(); };
        dialog.Show();
    }

    void IconMaker_Click(object sender, RoutedEventArgs e)
    {
        if (_iconMaker is { IsVisible: true }) { _iconMaker.Activate(); return; }
        var dialog = new IconMakerWindow(_preferences.Theme) { Owner = this };
        _iconMaker = dialog;
        dialog.Closed += (_, _) => { _iconMaker = null; _preview?.Rebuild(); };
        dialog.ShowDialog();
    }

    void Multiview_Click(object sender, RoutedEventArgs e)
    {
        var window = new MultiviewWindow(_session, _preferences, _scorebug, _engine, _publishedEngine,
            _program, _automation, _playback) { Owner = this };
        window.Show();
    }

    void ReplayBooth_Click(object sender, RoutedEventArgs e)
    {
        var window = new ReplayBoothWindow(_booth, _preferences) { Owner = this };
        window.Show();
    }

    void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (_operatorLocked) return;
        if (string.IsNullOrWhiteSpace(_preferences.OperatorLockPasswordHash))
        {
            var created = OperatorLockWindow.Create(this, _preferences.Theme);
            if (created is null) return;
            _preferences.OperatorLockPasswordHash = OperatorLock.Hash(created);
            PreferencesStore.Save(_preferences);
            SetOperatorLocked(true);
            return;
        }
        if (!OperatorLockWindow.Confirm(this, _preferences.Theme, _preferences.OperatorLockPasswordHash,
                "LOCK SCOREPLUS", "Enter the operator lock password to lock the console, media, rosters and remote.")) return;
        SetOperatorLocked(true);
    }

    void Unlock_Click(object? sender, RoutedEventArgs e)
    {
        var password = this.FindControl<TextBox>("UnlockPasswordInput");
        var error = this.FindControl<TextBlock>("UnlockError");
        if (password is null || error is null) return;
        if (!OperatorLock.Matches(password.Text, _preferences.OperatorLockPasswordHash))
        {
            error.Text = "That password does not match the operator lock.";
            password.SelectAll();
            password.Focus();
            return;
        }
        SetOperatorLocked(false);
    }

    void UnlockPassword_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Unlock_Click(sender, e);
    }

    void SetOperatorLocked(bool locked)
    {
        _operatorLocked = locked;
        Root.IsEnabled = _workspaceEnabled && !locked;
        LockButton.IsEnabled = _workspaceEnabled && !locked;
        Root.Opacity = locked ? 0.35 : _workspaceEnabled ? 1 : 0.55;
        LockOverlay.IsVisible = locked;
        LockButton.Content = locked ? "🔓" : "🔒";
        foreach (var panel in _controlPanels) panel.IsEnabled = !locked;
        var password = this.FindControl<TextBox>("UnlockPasswordInput");
        var error = this.FindControl<TextBlock>("UnlockError");
        if (password is not null) password.Text = string.Empty;
        if (error is not null) error.Text = string.Empty;
        if (locked)
        {
            Dispatcher.BeginInvoke(new Action(() => password?.Focus()));
        }
        else
        {
            _panel.Refresh();
        }
    }

    void ApplyHardware(DeviceSnapshot snapshot)
    {
        if (!_engine.ApplyHardwareSnapshot(snapshot) || !_liveScoreClock) return;
        if (!_publishedEngine.UpdateLiveController(_engine.State)) return;
        _preview?.Refresh();
        foreach (var output in _outputs.Where(output => output.IsVisible)) output.Publish();
    }

    void StartHardware()
    {
        if (!_preferences.Hardware.AutoConnect || _hardware.IsRunning) return;
        _hardware.Start(_preferences.Hardware);
    }

    void EventLog_Click(object sender, RoutedEventArgs e)
    {
        if (_log is { IsVisible: true }) { _log.Activate(); return; }
        _log = new EventLogWindow(_engine, _preferences.Theme, _preferences.ShowHeadingHints) { Owner = this };
        _log.Closed += (_, _) => _log = null;
        _log.Show();
    }

    void ScorebugStudio_Click(object sender, RoutedEventArgs e)
    {
        // The studio edits exactly the display and layout this window is previewing.
        var displayIndex = _scorebug.Displays.FindIndex(display => string.Equals(display.Id, _program.DisplayId, StringComparison.OrdinalIgnoreCase));
        if (displayIndex >= 0) _scorebug.SelectDisplay(displayIndex);
        _scorebug.SelectLayout(_program.LayoutIndex);
        var studio = new ScorebugEditorWindow(_scorebug, _engine, _playback, _preferences.Theme) { Owner = this };
        studio.ShowDialog();
        // The studio can add, delete or switch layouts; the preview and every panel follow.
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
        // A quick button may point at the layout that was just renamed or removed.
        _quickLayouts?.Refresh();
    }

    /// <summary>
    /// Opens one of the two motion makers on the display and layout this window is previewing, so
    /// what the operator builds applies to the graphic they are looking at.
    /// </summary>
    void OpenMotionMaker(MotionKind kind)
    {
        var displayIndex = _scorebug.Displays.FindIndex(display => string.Equals(display.Id, _program.DisplayId, StringComparison.OrdinalIgnoreCase));
        if (displayIndex >= 0) _scorebug.SelectDisplay(displayIndex);
        _scorebug.SelectLayout(_program.LayoutIndex);
        var maker = new MotionMakerWindow(kind, _scorebug, _engine, _preferences.Theme) { Owner = this };
        maker.ShowDialog();
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
        _quickLayouts?.Refresh();
    }

    void TransitionMaker_Click(object sender, RoutedEventArgs e) => OpenMotionMaker(MotionKind.Transition);

    void AnimationMaker_Click(object sender, RoutedEventArgs e) => OpenMotionMaker(MotionKind.Animation);

    async void Info_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutWindow(_preferences.Theme);
        await dialog.ShowDialog(this);
    }

    async void Ticker_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TickerWindow(_engine, _preferences.Theme);
        await dialog.ShowDialog(this);
    }

    async void LeagueCloud_Click(object sender, RoutedEventArgs e)
    {
        if (_leagueCloud is null) return;
        var dialog = new LeagueCloudWindow(_leagueCloud, _preferences, _scorebug, _program, _engine);
        await dialog.ShowDialog(this);
        _leagueCloud.ApplySettings();
    }

    void Preferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_preferences) { Owner = this };
        ThemeManager.Apply(dialog, _preferences.Theme);
        if (dialog.ShowDialog() != true) return; // Cancel discards: nothing is applied or saved.
        ThemeManager.Apply(this, _preferences.Theme);
        // The new theme paints buttons as it runs; put the active tab's outline back on top.
        ApplyTabVisuals();
        MediaControl.ApplyTheme(_preferences.Theme);
        RosterControl.ApplyTheme(_preferences.Theme);
        _macroDock?.ApplyTheme(_preferences.Theme);
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.ApplyTheme(_preferences.Theme);
        foreach (var output in _outputs) output.SetBroadcastTheme(_preferences.Theme);
        ApplyHintVisibility();
        _panel.SetHintVisibility(_preferences.ShowHeadingHints);
        // A changed value delay reaches every graphic already on screen, in this window and out.
        DisplayTiming.Apply(_preferences);
        // The quick buttons may have been switched between staging and publishing.
        _quickLayouts?.Refresh();
        _preview?.ApplyNow();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.RefreshPreview();
        foreach (var output in _outputs) output.RefreshGraphic();
        ApplyTabVisuals();
        // Turning the remote on or changing its password takes effect without a restart.
        StartRemote();
        _leagueCloud?.ApplySettings();
    }

    /// <summary>The library as the phone's Media tab lists it.</summary>
    string MediaSnapshotJson()
    {
        var live = new List<string>();
        var paused = new List<string>();
        void Add(List<string> list, string? label)
        {
            if (!string.IsNullOrWhiteSpace(label) && !live.Contains(label) && !paused.Contains(label)) list.Add(label);
        }
        if (_session.IsCurrentPlaying) Add(live, _session.Current?.Label);
        else Add(paused, _session.Current?.Label);
        if (_session.IsBackgroundPlaying) Add(live, _session.BackgroundAudio?.Label);
        else Add(paused, _session.BackgroundAudio?.Label);
        if (_session.Playback.IsPlaying)
            foreach (var file in _session.Playback.ActiveFiles) Add(live, file.Label);
        else
            foreach (var file in _session.Playback.ActiveFiles) Add(paused, file.Label);
        foreach (var camera in _session.LiveCameras)
            if (!Same(_session.Current, camera)) Add(live, camera.Label);
        foreach (var microphone in _session.LiveMicrophones)
            if (!Same(_session.Current, microphone)) Add(live, microphone.Label);
        var speakers = _session.LiveSpeakers.Select(speaker => speaker.Label).ToList();
        static bool Same(MediaEntry? left, MediaEntry right) =>
            left is not null && string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(new
        {
            current = string.Join(" + ", live.Count > 0 ? live : paused),
            live,
            paused,
            speakers,
            clips = _session.Library.Select(entry => new
            {
                id = entry.Path,
                label = entry.Label,
                kind = entry.KindLabel,
                onAir = _session.IsOnAir(entry)
            }).ToList()
        });
    }

    /// <summary>The three media buttons a phone can reach: on air, play/pause, and back off air.</summary>
    bool MediaAction(string action, string id)
    {
        var entry = _session.Library.FirstOrDefault(item => string.Equals(item.Path, id, StringComparison.OrdinalIgnoreCase));
        switch (action)
        {
            case "toggle": _session.TogglePlayPause(); return true;
            case "play" when entry is not null: _session.Play(entry); return true;
            case "onair" when entry is not null: _session.ToggleOnAir(entry); return true;
            default: return false;
        }
    }

    /// <summary>Both rosters, their players and the library, as the phone's Roster tab lists them.</summary>
    string RosterSnapshotJson()
    {
        var s = _engine.State;
        object Side(bool home)
        {
            var rosterId = home ? s.HomeRosterId : s.AwayRosterId;
            var roster = RosterStore.Book.Find(rosterId);
            var selected = (home ? s.HomePlayer : s.AwayPlayer)?.PlayerId;
            return new
            {
                name = home ? s.HomeTeam : s.AwayTeam,
                rosterId,
                rosterName = roster?.Name ?? string.Empty,
                players = (roster?.Players ?? []).Select(player => new
                {
                    id = player.Id,
                    name = player.Name,
                    number = player.Number,
                    position = player.Position,
                    group = player.Group,
                    stats = player.Stats.Select(stat => new { name = stat.Name, value = stat.Value, display = stat.Display }).ToList(),
                    selected = string.Equals(player.Id, selected, StringComparison.OrdinalIgnoreCase)
                }).ToList()
            };
        }
        return JsonSerializer.Serialize(new
        {
            home = Side(true),
            away = Side(false),
            rosters = RosterStore.All().Select(roster => new { id = roster.Id, name = roster.Name }).ToList()
        });
    }

    /// <summary>Assigns a roster or puts one player on the graphic, from the phone.</summary>
    bool RosterAction(string action, string side, string id, string stat, string value)
    {
        var home = !string.Equals(side, "away", StringComparison.OrdinalIgnoreCase);
        switch (action)
        {
            case "roster": _engine.AssignRoster(home, string.IsNullOrWhiteSpace(id) ? null : id); return true;
            case "player":
                if (string.IsNullOrWhiteSpace(id)) _engine.ClearSelectedPlayer(home);
                else _engine.SelectPlayer(home, id);
                return true;
            case "clear": _engine.ClearSelectedPlayer(home); return true;
            case "stat":
            {
                if (string.IsNullOrWhiteSpace(stat) || !int.TryParse(value, out var number)) return false;
                var roster = RosterStore.Book.Find(home ? _engine.State.HomeRosterId : _engine.State.AwayRosterId);
                var player = roster?.Find(id);
                if (player is null) return false;
                player.SetStat(stat, number);
                RosterStore.Save();
                _engine.ReloadSelectedPlayer(home);
                return true;
            }
            default: return false;
        }
    }

    /// <summary>The phone's flag card: choosing the type or team leaves the flag as it is.</summary>
    bool PenaltyAction(string action, string type, string side)
    {
        var home = !string.Equals(side, "away", StringComparison.OrdinalIgnoreCase);
        var chosen = string.IsNullOrWhiteSpace(type) ? _engine.State.PenaltyType : type;
        switch (action)
        {
            case "flag": _engine.SetPenalty(!_engine.State.PenaltyActive, chosen, home); return true;
            case "clear": _engine.SetPenalty(false, chosen, home); return true;
            case "type":
            case "team": _engine.SetPenalty(_engine.State.PenaltyActive, chosen, home); return true;
            default: return false;
        }
    }

    /// <summary>What a browser watching /watch should see: the program layout and published game.</summary>
    (ScorebugLayout Layout, GameState State)? WatchSnapshot() =>
        (_automation.Effective(_program.DisplayId, _scorebug.PublishedCompositeOf(_program.DisplayId)), _publishedEngine.State);

    /// <summary>Resolves /watch?display=2, a display id, or a display name; no key means this window's display.</summary>
    ScorebugDisplay? ResolveWatchDisplay(string? key)
    {
        if (_scorebug.Displays.Count == 0) return null;
        var fallback = _scorebug.FindDisplay(_program.DisplayId) ?? _scorebug.Displays[0];
        if (string.IsNullOrWhiteSpace(key)) return fallback;
        string clean;
        try { clean = Uri.UnescapeDataString(key).Trim(); }
        catch { clean = key.Trim(); }
        if (int.TryParse(clean, out var number) && number >= 1 && number <= _scorebug.Displays.Count)
            return _scorebug.Displays[number - 1];
        return _scorebug.FindDisplay(clean)
            ?? _scorebug.Displays.FirstOrDefault(display => string.Equals(display.Name, clean, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    /// <summary>One off-screen render of the program layout, encoded as PNG for the browser.</summary>
    byte[]? RenderProgramPng(ScorebugLayout layout)
    {
        ScorebugCanvasView? view = null;
        try
        {
            var scale = Math.Clamp((int)Math.Ceiling(1920.0 / Math.Max(1, layout.Width)), 1, 3);
            if (layout.Width * scale > 3840) scale = 1;
            var width = Math.Max(1, layout.Width) * scale;
            var height = Math.Max(1, layout.Height) * scale;
            view = new ScorebugCanvasView(_scorebug, _engine,
                stateProvider: () => _publishedEngine.State, playback: null, layoutProvider: () => layout)
            {
                Width = width,
                Height = height
            };
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));
            var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            bitmap.Render(view);
            using var buffer = new MemoryStream();
            bitmap.Save(buffer);
            return buffer.ToArray();
        }
        catch { return null; }
        finally { view?.Dispose(); }
    }

    /// <summary>The program graphic as a PNG, drawn by the same view the audience window draws.</summary>
    (byte[] Png, long Version)? WatchImageSnapshot(string? displayKey, string? layoutKey = null)
    {
        var display = ResolveWatchDisplay(displayKey);
        if (display is null) return null;
        var chosen = !string.IsNullOrWhiteSpace(layoutKey) ? _scorebug.FindLayout(layoutKey) : null;
        var layout = chosen is not null
            ? _automation.Effective(display.Id, chosen)
            : _automation.Effective(display.Id, _scorebug.PublishedCompositeOf(display.Id));
        var cacheKey = display.Id + "|" + (layoutKey ?? "");
        var state = _publishedEngine.State;
        var signature = JsonSerializer.Serialize(layout) + "|" + GameEngine.StagedSignature(state);
        if (!_watchFrames.TryGetValue(cacheKey, out var cached) || !string.Equals(cached.Signature, signature, StringComparison.Ordinal))
        {
            _watchVersion++;
            cached = (signature, null, _watchVersion);
        }
        cached.Png ??= RenderProgramPng(layout);
        if (cached.Png is null) return null;
        _watchFrames[cacheKey] = cached;
        return (cached.Png, cached.Version);
    }

    /// <summary>
    /// Starts (or restarts) the phone remote from the saved preferences. It is off unless enabled and
    /// protected by a password, so an unattended install never exposes a control surface.
    /// </summary>
    void StartRemote()
    {
        _remote?.Dispose();
        _remote = null;
        RemoteControlServer.WatchSource = null;
        RemoteControlServer.WatchImage = null;
        RemoteControlServer.WatchLayoutImage = null;
        RemoteControlServer.MediaJson = null;
        RemoteControlServer.MediaAction = null;
        RemoteControlServer.RosterJson = null;
        RemoteControlServer.RosterAction = null;
        RemoteControlServer.PenaltyAction = null;
        RemoteControlServer.LockState = () => _operatorLocked;
        RemoteControlServer.MediaJson = MediaSnapshotJson;
        RemoteControlServer.MediaAction = MediaAction;
        RemoteControlServer.RosterJson = RosterSnapshotJson;
        RemoteControlServer.RosterAction = RosterAction;
        RemoteControlServer.PenaltyAction = PenaltyAction;
        if (!_preferences.RemoteEnabled || string.IsNullOrWhiteSpace(_preferences.RemotePassword)) return;
        var server = new RemoteControlServer(_engine, this, id => { _panel.Execute(id); return true; }, Avalonia.Threading.Dispatcher.UIThread, _preferences.RemotePort, _preferences.RemotePassword);
        // The audience feed draws the published program, on the UI thread: the program selection and
        // the published game belong to this window, and the server asks from a background thread.
        RemoteControlServer.WatchImage = display =>
        {
            (byte[] Png, long Version)? result = null;
            Dispatcher.Invoke(() => result = WatchImageSnapshot(display));
            return result;
        };
        RemoteControlServer.WatchLayoutImage = (display, layout) =>
        {
            (byte[] Png, long Version)? result = null;
            Dispatcher.Invoke(() => result = WatchImageSnapshot(display, layout));
            return result;
        };
        server.Status += message => Dispatcher.BeginInvoke(new Action(() => StatusText.Text = message));
        server.Start();
        _remote = server;
    }

    /// <summary>
    /// Looks for a newer installed build, then downloads, verifies and starts its installer. Only an
    /// installed copy checks; a folder build stays exactly as it is.
    /// </summary>
    async Task CheckForUpdatesAsync()
    {
        if (!CheckForUpdatesOnLoad || !_preferences.CheckForUpdates || !AppInfo.CanUpdate) return;
        var manifest = await UpdateChecker.CheckAsync(_preferences.UpdateFeedUrl, AppInfo.Kind);
        if (manifest is null) return;
        var detail = $"Version {manifest.Version} is ready (this build is {AppInfo.Version}). " +
                     (string.IsNullOrWhiteSpace(manifest.Notes) ? "Download and install it now?" : $"{manifest.Notes}\n\nDownload and install it now?");
        if (!ConfirmWindow.Ask(this, _preferences.Theme, "UPDATE AVAILABLE", detail, "Download and install")) return;
        StatusText.Text = $"UPDATING · downloading {manifest.Version}";
        var (path, error) = await UpdateChecker.DownloadAsync(manifest);
        if (path is null)
        {
            StatusText.Text = "UPDATE FAILED";
            NoticeWindow.Tell(this, "UPDATE FAILED", error ?? "The update could not be downloaded.", warning: true);
            return;
        }
        if (UpdateChecker.Run(path) is string failure)
        {
            StatusText.Text = "UPDATE COULD NOT START";
            NoticeWindow.Tell(this, "UPDATE COULD NOT START", failure, warning: true);
            return;
        }
        _closingAfterPrompt = true;
        Close();
    }

    void NewPanel_Click(object sender, RoutedEventArgs e) => OpenControlPanel();

    void Keybinds_Click(object sender, RoutedEventArgs e) => _panel.ShowKeybindsDialog();

    public void OpenControlPanel()
    {
        var panel = new GameControlWindow(_engine, _publishedEngine, _scorebug, PublishGameState, HasStagedGameState,
            SetLiveScoreClock, () => _liveScoreClock, _session, _preferences.Theme);
        panel.NewPanelRequested += OpenControlPanel;
        panel.Closed += (_, _) => _controlPanels.Remove(panel);
        panel.ApplyTheme(_preferences.Theme);
        panel.SetHintVisibility(_preferences.ShowHeadingHints);
        _controlPanels.Add(panel);
        panel.Show();
    }

    /// <summary>True when the game data differs from what is on air, ignoring any layout choice.</summary>
    public bool HasStagedGameState() =>
        GameEngine.StagedSignature(_engine.State) != GameEngine.StagedSignature(_publishedEngine.State);

    void SportProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SportProfileWindow(_engine.Sports) { Owner = this };
        ThemeManager.Apply(dialog, _preferences.Theme);
        dialog.ShowDialog();
        var activeId = SportSelector.SelectedValue as string;
        _sportSync = true;
        try
        {
            RefreshSports();
            if (_engine.Sports.Find(activeId) is not null) SportSelector.SelectedValue = activeId;
        }
        finally { _sportSync = false; }
        if (!dialog.Changed) return;
        // The catalog was edited in place, so the console and any detached panel rebuild from it
        // now; only a new game re-reads the rules (timeouts, scoring buttons, period length).
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
        StatusText.Text = $"SPORT PROFILES SAVED · {_engine.Sport.Name} · start a new game to use the new rules";
    }

    void GameName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_initializing || _gameNameSync) return;
        _engine.SetGameName(GameNameBox.Text);
    }

    void Sport_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _sportSync || SportSelector.SelectedValue is not string id) return;
        // A sport switch starts a brand new game, so anything the old sport was tracking is about to
        // go. Say so plainly instead of deleting a period's worth of counts without a word.
        if (WarnOnSportSwitch && _engine.HasSportSpecificData())
        {
            var next = _engine.Sports.Find(id)?.Name ?? id;
            var detail = $"Switching to {next} starts a new game. The {_engine.Sport.Name} values this game is tracking — its counts, flags and figures — are cleared with it, along with the score, clocks and custom stats. The roster library, sport profiles, media and your layouts stay.";
            if (!ConfirmWindow.Ask(this, _preferences.Theme, "SWITCH SPORT", detail, "Switch sport", destructive: true))
            {
                // Put the picker back on the sport being played; nothing has been touched yet.
                _sportSync = true;
                try { SportSelector.SelectedValue = _engine.State.SportId; }
                finally { _sportSync = false; }
                return;
            }
        }
        StartNewGame(id, announce: false);
        StatusText.Text = $"NEW {_engine.Sport.Name.ToUpperInvariant()} GAME";
    }

    void NewGame_Click(object sender, RoutedEventArgs e)
    {
        // The sport is part of starting a game; the chooser also states what a new game resets.
        var picker = new SportPickerWindow(_engine.Sports, _engine.State.SportId, _preferences.Theme) { Owner = this };
        if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SportId)) return;
        StartNewGame(picker.SportId);
    }

    /// <summary>
    /// A new game resets every piece of game state and takes the audience output with it, so nothing
    /// from the previous game survives on air. Operator settings that are not part of a game — sport
    /// profiles, keybinds, preferences, the media library and the scorebug design — are untouched.
    /// </summary>
    public void StartNewGame(string sportId, bool announce = true)
    {
        _autoHornReady = false;
        _engine.NewGame(sportId);
        _projectPath = null;
        _startNewAutosavePoint = true;
        _savedStateJson = _engine.ExportJson();
        _preferences.LastSport = sportId;
        try { PreferencesStore.Save(_preferences); } catch { }
        // A new game really starts over: one factory layout and no folder/playlist organisation.
        // Imported media files stay in the library; the game's design and shelves begin blank.
        _scorebug.ResetToDefault();
        _savedDesignJson = DesignJson();
        _program.Sync();
        SyncDisplayPicker();
        _session.ResetOrganization();
        MediaControl.ResetView();
        foreach (var panel in _controlPanels.Where(panel => panel.IsVisible)) panel.Media.ResetView();
        ResetOutputToCurrentGame();
        RefreshLayoutIndicators();
        _macroDock?.RefreshView(force: true);
        if (announce) StatusText.Text = $"NEW {_engine.Sport.Name.ToUpperInvariant()} GAME";
    }

    /// <summary>
    /// Puts the published copy (preview and audience output) on the same fresh state and takes any
    /// cued clip off the scorebug, so a new game really is a blank slate on air.
    /// </summary>
    void ResetOutputToCurrentGame()
    {
        _publishedEngine.LoadFromState(_engine.CloneState());
        // Rebuild, not refresh: a new game gets brand-new visuals, so no score, down, possession
        // or last video frame can be carried over from the game before it.
        _preview?.Rebuild();
        _audience?.Refresh();
        foreach (var output in _outputs) { output.Publish(); output.Rebuild(); }
        _panel.Refresh();
        // Take the cued clip off the scorebug last, so a media hiccup cannot block the game reset.
        _session.Stop();
    }

    bool LoadGame(string path)
    {
        try
        {
            _autoHornReady = false;
            var design = _engine.Load(path);
            // A file written by an older build has no design; the current scorebug then stays as it
            // is rather than being replaced by a factory layout.
            if (design is not null) _scorebug.ApplyDocument(design);
            ApplySavedQuickButtons();
            _projectPath = path;
            _startNewAutosavePoint = true;
            // Point the picker at the loaded sport without letting that look like a sport change,
            // which would start a brand new game and throw the loaded one away.
            _sportSync = true;
            try
            {
                RefreshSports();
                SportSelector.SelectedValue = _engine.State.SportId;
            }
            finally { _sportSync = false; }
            _publishedEngine.LoadFromState(_engine.CloneState());
            _program.Sync();
            SyncDisplayPicker();
            // Rebuild, not refresh: visuals made before this game was read (for example a logo
            // element built while the teams had no crests) must take the loaded values.
            _preview?.Rebuild();
            foreach (var output in _outputs) output.Rebuild();
            _macroDock?.RefreshView(force: true);
            _savedStateJson = _engine.ExportJson();
            _savedDesignJson = DesignJson();
            StatusText.Text = $"GAME LOADED · {System.IO.Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"COULD NOT OPEN GAME · {ex.Message}";
            return false;
        }
    }

    // ---------- output ----------
    void Output_Click(object sender, RoutedEventArgs e) => OpenOutputWindow();

    void Annotate_Click(object sender, RoutedEventArgs e) => OpenOutputWindow(annotations: true);

    /// <summary>The shared ink board for a display, with the operator's saved fade time.</summary>
    AnnotationBoard BoardFor(string displayId)
    {
        if (_annotationBoards.TryGetValue(displayId, out var existing)) return existing;
        var seconds = PreferencesStore.Load().AnnotationFadeSeconds;
        var hold = seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Clamp(seconds, 0.5, 10));
        var board = new AnnotationBoard(hold);
        _annotationBoards[displayId] = board;
        return board;
    }

    /// <summary>
    /// Asks which display this audience window is for, then opens a brand-new window for it. With
    /// only one display there is nothing to ask, so the question is skipped. Annotation reuses an
    /// audience window already open for that display, and mirrors its strokes to every other output
    /// on the same display, so the pen is genuinely on the audience picture.
    /// </summary>
    public OutputWindow? OpenOutputWindow(string? displayId = null, bool annotations = false)
    {
        if (displayId is null)
        {
            if (_scorebug.Displays.Count == 1) displayId = _scorebug.Displays[0].Id;
            else
            {
                displayId = DisplayPickerWindow.Ask(this, _scorebug, _preferences.Theme, _program.DisplayId);
                if (displayId is null) return null;
            }
        }
        var board = BoardFor(displayId);
        if (annotations)
        {
            var existing = _outputs.FirstOrDefault(output => output.IsVisible
                && string.Equals(output.DisplayId, displayId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                board.Open();
                foreach (var other in _outputs.Where(output => output.IsVisible
                    && string.Equals(output.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)))
                    other.ShowAudienceInk();
                existing.OpenAnnotationTools();
                existing.Activate();
                return existing;
            }
        }
        var output = new OutputWindow(_publishedEngine, _scorebug, _preferences.Theme, _playback, displayId, annotations, board)
        {
            Title = $"ScorePlus — {_scorebug.FindDisplay(displayId)?.Name ?? "Program"} Audience Output"
        };
        _outputs.Add(output);
        output.Closed += (_, _) => _outputs.Remove(output);
        output.Show();
        if (string.Equals(displayId, _program.DisplayId, StringComparison.OrdinalIgnoreCase))
            output.PrepareTransition(_program.Layout);
        if (annotations)
        {
            foreach (var other in _outputs.Where(candidate => !ReferenceEquals(candidate, output) && candidate.IsVisible
                && string.Equals(candidate.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)))
                other.ShowAudienceInk();
        }
        else if (board.IsOpen)
        {
            output.ShowAudienceInk();
        }
        // Cascade each new window instead of stacking it exactly on top of the last one, so every
        // output opened this session stays visibly on screen. Position is only known after Show.
        var step = 32 * (_outputs.Count - 1);
        if (step > 0)
        {
            output.WindowStartupLocation = WindowStartupLocation.Manual;
            output.Left += step;
            output.Top += step;
        }
        output.Activate();
        if (_liveScoreClock) output.UpdateLiveScoreClock(_engine.State);
        else output.RefreshDisplay();
        return output;
    }

    void SetLiveScoreClock(bool enabled)
    {
        _liveScoreClock = enabled;
        foreach (var panel in _controlPanels) panel.SyncLiveState(enabled);
        _panel.SyncLiveState(enabled);
        if (enabled) PublishLiveValues();
    }

    void EngineChanged()
    {
        ReplayBoothHistory.Sample(_engine.State);
        CheckAutoHorns();
        RefreshEndGameButton();
        // New series can ask to go on air by itself; publish the whole staged state in that case.
        var publishRequested = _engine.TakePublishRequest();
        if (_engine.MacroOutputHold) return;
        if (publishRequested) { PublishGameState(); return; }
        if (_liveScoreClock) PublishLiveValues();
    }

    /// <summary>
    /// Sounds the selected horn when a running game or play clock reaches zero. The edge is what
    /// matters: a clock loaded at zero or stopped by hand must not fire.
    /// </summary>
    void CheckAutoHorns()
    {
        var state = _engine.State;
        if (!_autoHornReady)
        {
            _autoHornReady = true;
            _lastGameClock = state.ClockSeconds;
            _lastPlayClock = state.PlayClockSeconds;
            _lastGameClockRunning = state.ClockRunning;
            _lastPlayClockRunning = state.PlayClockRunning;
            return;
        }
        var gameExpired = _preferences.HornOnGameClockZero
            && !state.ClockCountsUp
            && state.ClockSeconds == 0
            && _lastGameClock > 0
            && _lastGameClockRunning
            && !state.ClockRunning;
        var playExpired = _preferences.HornOnPlayClockZero
            && state.PlayClockSeconds == 0
            && _lastPlayClock > 0
            && _lastPlayClockRunning
            && !state.PlayClockRunning;
        if (gameExpired || playExpired)
            HornPlayer.Play(_preferences.HornSound, _preferences.HornCustomPath);
        _lastGameClock = state.ClockSeconds;
        _lastPlayClock = state.PlayClockSeconds;
        _lastGameClockRunning = state.ClockRunning;
        _lastPlayClockRunning = state.PlayClockRunning;
    }

    /// <summary>The header button follows the game: End Game until the final, then Restart Game.</summary>
    void RefreshEndGameButton()
    {
        if (EndGameButton is null) return;
        var final = _engine.State.GameEnded && _engine.State.RecordSettled;
        var content = final ? "Restart Game" : "End Game";
        if (!Equals(EndGameButton.Content, content)) EndGameButton.Content = content;
        EndGameButton.ToolTip = final
            ? "Start the same game over: the score, clocks and game stats reset; the teams, records and design stay"
            : _engine.State.GameEnded
                ? "Add this final result to each team's record"
                : "Stop every clock, mark the game final and add the result to each team's record";
    }

    /// <summary>
    /// Ends the game on command, for a game called before the clock expires. A settled final
    /// restarts the same fixture instead of ending it a second time.
    /// </summary>
    void EndGame_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.State.GameEnded && _engine.State.RecordSettled)
        {
            if (!ConfirmWindow.Ask(this, _preferences.Theme, "RESTART GAME",
                "Start the same game over? The score, clocks, period and stats reset while the teams and design stay.",
                "Start fresh")) return;
            if (_engine.RestartGame())
            {
                _publishedEngine.LoadFromState(_engine.CloneState());
                _program.Sync();
                ResetOutputToCurrentGame();
                _savedStateJson = _engine.ExportJson();
                StatusText.Text = "GAME RESTARTED · same teams, fresh score and clock";
                AutoSave();
            }
            return;
        }
        var homeRecord = _engine.State.HomeRecord;
        var awayRecord = _engine.State.AwayRecord;
        var settledBefore = _engine.State.RecordSettled;
        if (!_engine.EndGame())
        {
            StatusText.Text = "GAME ENDED · start a New Game to play again";
            return;
        }
        StatusText.Text = _engine.State.ClockMessage == GameClockMessage.EndOfRegulation
            ? "TIED · END OF REG. · advance to overtime or choose a tie rule in Preferences"
            : homeRecord != _engine.State.HomeRecord || awayRecord != _engine.State.AwayRecord
                ? "GAME ENDED · team records updated"
                : settledBefore ? "GAME ENDED · records were already updated" : "GAME ENDED · no team records were set";
        AutoSave();
    }

    void PublishLiveValues()
    {
        if (!_publishedEngine.UpdateLiveScoreClock(_engine.State)) return;
        _preview?.Refresh();
        foreach (var output in _outputs.Where(o => o.IsVisible)) output.UpdateLiveScoreClock(_engine.State);
    }

    void GameReport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "HTML game report (*.html)|*.html", DefaultExt = "html", FileName = $"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}-report" };
        if (dialog.ShowDialog() != true) return;
        // The report carries every player on every roster, including the ones who never touched the
        // ball, so a zero is printed rather than an empty table.
        File.WriteAllText(dialog.FileName, GameReport.Build(_engine.State, RosterStore.Book.Rosters));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        StatusText.Text = "GAME REPORT EXPORTED";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "ScorePlus Game (*.spg)|*.spg|JSON (*.json)|*.json", DefaultExt = "spg", FileName = $"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}" };
        if (dialog.ShowDialog() != true) return;
        SaveGameTo(dialog.FileName);
    }

    /// <summary>Copies the live quick buttons into the game state so they travel with the save.</summary>
    void CaptureQuickButtons()
    {
        _macroDock?.CommitPendingEdits();
        var preferences = PreferencesStore.Load();
        _engine.State.QuickLayouts = (preferences.QuickLayouts ?? []).Select(binding => new SavedQuickLayout
        {
            Label = binding.Label,
            DisplayId = binding.DisplayId,
            LayoutId = binding.LayoutId
        }).ToList();
        _engine.State.QuickMedia = (preferences.QuickMedia ?? []).Select(binding => new SavedQuickMedia
        {
            Label = binding.Label,
            Path = binding.Path
        }).ToList();
        _engine.State.QuickButtonsSaved = true;
    }

    /// <summary>Restores the quick buttons a loaded game carried, and refreshes both bars.</summary>
    void ApplySavedQuickButtons()
    {
        var layouts = _engine.State.QuickLayouts ?? [];
        var media = _engine.State.QuickMedia ?? [];
        // A save from before this feature carries neither list; keep the operator's current buttons.
        if (!_engine.State.QuickButtonsSaved && layouts.Count == 0 && media.Count == 0) return;
        var preferences = PreferencesStore.Load();
        preferences.QuickLayouts = layouts.Select(binding => new QuickLayoutBinding
        {
            Label = binding.Label,
            DisplayId = binding.DisplayId,
            LayoutId = binding.LayoutId
        }).ToList();
        preferences.QuickMedia = media.Select(binding => new QuickMediaBinding
        {
            Label = binding.Label,
            Path = binding.Path
        }).ToList();
        PreferencesStore.Save(preferences);
        _preferences.QuickLayouts = preferences.QuickLayouts;
        _preferences.QuickMedia = preferences.QuickMedia;
        _quickLayouts?.Refresh();
        _quickMedia?.Refresh();
    }

    /// <summary>Writes the game and remembers that this is now the saved state.</summary>
    bool SaveGameTo(string path)
    {
        try
        {
            // A macro being typed is part of the game too; commit it before writing the file.
            _macroDock?.CommitPendingEdits();
            CaptureQuickButtons();
            // The game travels with its screens: every display, every layout and what each one has
            // on air, so opening the file puts the show back exactly where it was left.
            _engine.Save(path, _scorebug.ExportDocument());
            _projectPath = path;
            _savedStateJson = _engine.ExportJson();
            _savedDesignJson = DesignJson();
            StatusText.Text = $"GAME SAVED · {System.IO.Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"COULD NOT SAVE GAME · {ex.Message}";
            return false;
        }
    }

    bool HasUnsavedChanges() =>
        (_savedStateJson is not null && _savedStateJson != _engine.ExportJson())
        || (_savedDesignJson is not null && _savedDesignJson != DesignJson());

    /// <summary>A comparable snapshot of the design, for the unsaved-changes check.</summary>
    string DesignJson() => System.Text.Json.JsonSerializer.Serialize(_scorebug.ExportDocument());

    /// <summary>Saves to the game's own file, or asks for one when the game has never been saved.</summary>
    bool SaveBeforeClosing()
    {
        if (!string.IsNullOrWhiteSpace(_projectPath)) return SaveGameTo(_projectPath);
        var dialog = new SaveFileDialog
        {
            Filter = "ScorePlus Game (*.spg)|*.spg|JSON (*.json)|*.json",
            DefaultExt = "spg",
            FileName = $"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}"
        };
        return dialog.ShowDialog() == true && SaveGameTo(dialog.FileName);
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { DefaultExt = "spg", Filter = "ScorePlus Game (*.spg)|*.spg|Scoreboard Game (*.scoreboard;*.json)|*.scoreboard;*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        LoadGame(dialog.FileName);
    }

    // ---------- lifecycle ----------
    void AutoSave()
    {
        try
        {
            AutosaveStore.Save(_engine, _startNewAutosavePoint, _scorebug.ExportDocument());
            _startNewAutosavePoint = false;
            AutosaveText.Text = $"AUTOSAVED {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { StatusText.Text = $"AUTOSAVE WARNING · {ex.Message}"; }
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine.Changed -= Refresh;
        _engine.Changed -= EngineChanged;
        _remote?.Dispose();
        _leagueCloud?.Dispose();
        _hardware.Dispose();
        _booth.Dispose();
        _automation.Dispose();
        _automationWindow?.Close();
        _iconMaker?.Close();
        _meterTimer.Stop();
        _macroDock?.CommitPendingEdits();
        _program.Dispose();
        _preview?.Dispose();
        _quickMedia = null;
        _session.Dispose();
        foreach (var panel in _controlPanels.ToArray()) panel.Close();
        foreach (var output in _outputs.ToArray()) output.Close();
        _log?.Close();
        base.OnClosed(e);
    }
}
