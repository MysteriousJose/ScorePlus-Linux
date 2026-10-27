using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public partial class MainWindow : Window, IControlPanelHost
{
    readonly GameEngine _engine = new();
    readonly GameEngine _publishedEngine = new();
    readonly ScorebugCatalog _scorebug = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly List<GameControlWindow> _controlPanels = [];
    readonly ControlPanelView _panel;
    readonly MediaSession _session;
    readonly MediaPlayback _playback;
    // Every audience window ever opened this session. Opening the output again adds another one
    // instead of focusing the old window, so a show can spread the graphic over several displays.
    readonly List<OutputWindow> _outputs = [];
    EventLogWindow? _log;
    ScorebugCanvasView? _preview;
    int _eventCount = -1;
    readonly UserPreferences _preferences = PreferencesStore.Load();
    string? _projectPath;
    bool _initializing = true;
    bool _liveScoreClock = true;
    // Set while the sport picker is being filled in for the operator, so syncing the control cannot
    // be mistaken for the operator choosing a sport (which starts a new game).
    bool _sportSync;
    // The next autosave starts a fresh recoverable point instead of updating the newest one. Set
    // when a new game starts or a game is loaded, so the previous game keeps its own autosave.
    bool _startNewAutosavePoint = true;
    // The game state as it was the last time it went into a .spg file (or started fresh).
    string? _savedStateJson;
    bool _closingAfterPrompt;

    /// <summary>
    /// The exit prompt is on for operators. The UI checks turn it off because they create and close
    /// windows deliberately, and a modal prompt would stall an unattended run.
    /// </summary>
    public static bool PromptOnExit { get; set; } = true;

    /// <param name="mode">How the app was started: <see cref="LaunchMode.New"/> skips the autosave,
    /// <see cref="LaunchMode.Open"/> loads <paramref name="gamePath"/>. Null keeps the legacy
    /// behaviour of restoring the last autosave, which the tests rely on.</param>
    public MainWindow(LaunchMode? mode = null, string? gamePath = null)
    {
        InitializeComponent();
        _session = new MediaSession(_preferences);
        _playback = _session.Playback;
        ThemeManager.Apply(this, _preferences.Theme);
        SportSelector.SelectedValuePath = "Id";
        RefreshSports();
        SportSelector.SelectedValue = _engine.State.SportId;

        _engine.Changed += Refresh;
        _engine.Changed += EngineChanged;
        _timer.Tick += (_, _) => _engine.Tick();
        _timer.Start();
        _autosave.Tick += (_, _) => AutoSave();
        _autosave.Start();

        _preview = new ScorebugCanvasView(_scorebug, _publishedEngine, playback: _playback);
        PreviewHost.Content = _preview;
        RefreshLayoutIndicators();

        _panel = new ControlPanelView(_engine, this);
        ControlHost.Content = _panel;
        _panel.SetHintVisibility(_preferences.ShowHeadingHints);

        MediaControl.Attach(_session);
        MediaControl.ApplyTheme(_preferences.Theme);
        _session.Status += message => StatusText.Text = message;
        ApplyHintVisibility();

        Refresh();
        _initializing = false;

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

        // Panels are opened deliberately with New Panel; starting a game never spawns a second window.
    }

    /// <summary>The live game, exposed so the operator surface (and the checks) can drive it.</summary>
    public GameEngine Engine => _engine;

    /// <summary>The copy currently on air (preview and audience output).</summary>
    public GameState PublishedState => _publishedEngine.State;

    /// <summary>The console embedded in this window, exposed for the checks.</summary>
    public ControlPanelView Panel => _panel;

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
        _scorebug.ActiveIndex != _scorebug.PublishedIndex;

    /// <summary>The layout the preview is showing: the one that Update Output will publish.</summary>
    public string LayoutLabel => $"{_scorebug.ActiveIndex + 1} · {_scorebug.Layout.Name}";

    public void OpenNewPanel() => OpenControlPanel();

    public void PublishOutput()
    {
        // The chosen layout goes on air with the game data: the preview and output still read
        // different copies, so the switch only happens here.
        _scorebug.PublishLayout();
        _publishedEngine.LoadFromState(_engine.CloneState());
        _preview?.Refresh();
        foreach (var output in _outputs) output.Publish();
        StatusText.Text = $"OUTPUT UPDATED · {_scorebug.Layout.Name.ToUpperInvariant()} · {DateTime.Now:HH:mm:ss}";
        RefreshLayoutIndicators();
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
    }

    /// <summary>Moves the previewed layout; nothing goes on air until Update Output is pressed.</summary>
    public void ShiftLayout(int delta)
    {
        if (_scorebug.Layouts.Count == 0) return;
        var next = (_scorebug.ActiveIndex + delta + _scorebug.Layouts.Count) % _scorebug.Layouts.Count;
        _scorebug.SelectLayout(next);
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        StatusText.Text = _scorebug.PublishedIndex == _scorebug.ActiveIndex
            ? $"LAYOUT · {LayoutLabel} · ON AIR"
            : $"LAYOUT · {LayoutLabel} · preview only — press Update Output";
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
    }

    void RefreshLayoutIndicators()
    {
        PreviewLayoutLabel.Text = LayoutLabel;
        LayoutPrevButton.IsEnabled = LayoutNextButton.IsEnabled = _scorebug.Layouts.Count > 1;
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
        _eventCount = s.Events.Count;
    }

    void RefreshSports() => SportSelector.ItemsSource = _engine.Sports.All;

    void ApplyHintVisibility()
    {
        var visibility = _preferences.ShowHeadingHints ? Visibility.Visible : Visibility.Collapsed;
        SportHint.Visibility = PreviewAudienceHint.Visibility = visibility;
        MediaControl.SetHintVisibility(_preferences.ShowHeadingHints);
        _log?.SetHintVisibility(_preferences.ShowHeadingHints);
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
        var studio = new ScorebugEditorWindow(_scorebug, _engine, _playback, _preferences.Theme) { Owner = this };
        studio.ShowDialog();
        // The studio can add, delete or switch layouts; the preview and every panel follow.
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
    }

    void Preferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_preferences) { Owner = this };
        ThemeManager.Apply(dialog, _preferences.Theme);
        if (dialog.ShowDialog() != true) return; // Cancel discards: nothing is applied or saved.
        ThemeManager.Apply(this, _preferences.Theme);
        MediaControl.ApplyTheme(_preferences.Theme);
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.ApplyTheme(_preferences.Theme);
        foreach (var output in _outputs) output.SetBroadcastTheme(_preferences.Theme);
        ApplyHintVisibility();
        _panel.SetHintVisibility(_preferences.ShowHeadingHints);
    }

    void NewPanel_Click(object sender, RoutedEventArgs e) => OpenControlPanel();

    void Keybinds_Click(object sender, RoutedEventArgs e) => _panel.ShowKeybindsDialog();

    public void OpenControlPanel()
    {
        var panel = new GameControlWindow(_engine, this, _session);
        panel.NewPanelRequested += OpenControlPanel;
        panel.Closed += (_, _) => _controlPanels.Remove(panel);
        panel.ApplyTheme(_preferences.Theme);
        panel.Panel.SetHintVisibility(_preferences.ShowHeadingHints);
        _controlPanels.Add(panel);
        panel.Show();
    }

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

    void Sport_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _sportSync || SportSelector.SelectedValue is not string id) return;
        StartNewGame(id, announce: false);
        StatusText.Text = $"NEW {_engine.Sport.Name.ToUpperInvariant()} GAME";
    }

    void NewGame_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Start a new game? This resets everything about the game — score, clocks, down, possession, stats and the on-air graphic. Your scorebug design is kept.", "New game", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        StartNewGame(SportSelector.SelectedValue as string ?? "basketball");
    }

    /// <summary>
    /// A new game resets every piece of game state and takes the audience output with it, so nothing
    /// from the previous game survives on air. Operator settings that are not part of a game — sport
    /// profiles, keybinds, preferences, the media library and the scorebug design — are untouched.
    /// </summary>
    public void StartNewGame(string sportId, bool announce = true)
    {
        _engine.NewGame(sportId);
        _projectPath = null;
        _startNewAutosavePoint = true;
        _savedStateJson = _engine.ExportJson();
        _preferences.LastSport = sportId;
        try { PreferencesStore.Save(_preferences); } catch { }
        // A new game really starts over: one factory layout and no folder/playlist organisation.
        // Imported media files stay in the library; the game's design and shelves begin blank.
        _scorebug.ResetToDefault();
        _session.ResetOrganization();
        MediaControl.ResetView();
        foreach (var panel in _controlPanels.Where(panel => panel.IsVisible)) panel.Media.ResetView();
        ResetOutputToCurrentGame();
        RefreshLayoutIndicators();
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
        foreach (var output in _outputs) { output.Publish(); output.Rebuild(); }
        _panel.Refresh();
        // Take the cued clip off the scorebug last, so a media hiccup cannot block the game reset.
        _session.Stop();
    }

    bool LoadGame(string path)
    {
        try
        {
            _engine.Load(path);
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
            // Rebuild, not refresh: visuals made before this game was read (for example a logo
            // element built while the teams had no crests) must take the loaded values.
            _preview?.Rebuild();
            foreach (var output in _outputs) output.Rebuild();
            _savedStateJson = _engine.ExportJson();
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

    /// <summary>Opens a brand-new audience window; existing ones keep running alongside it.</summary>
    OutputWindow OpenOutputWindow()
    {
        var output = new OutputWindow(_publishedEngine, _scorebug, _preferences.Theme, _playback)
        {
            Title = $"ScorePlus — Audience Output {_outputs.Count + 1}"
        };
        _outputs.Add(output);
        output.Closed += (_, _) => _outputs.Remove(output);
        output.Show();
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

    void EngineChanged() { if (_liveScoreClock) PublishLiveValues(); }

    void PublishLiveValues()
    {
        if (!_publishedEngine.UpdateLiveScoreClock(_engine.State)) return;
        _preview?.Refresh();
        foreach (var output in _outputs.Where(o => o.IsVisible)) output.UpdateLiveScoreClock(_engine.State);
    }

    void Fullscreen_Click(object sender, RoutedEventArgs e) => OpenOutputWindow().ToggleFullscreen();

    void GameReport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "HTML game report (*.html)|*.html", DefaultExt = "html", FileName = $"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}-report" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, GameReport.Build(_engine.State));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        StatusText.Text = "GAME REPORT EXPORTED";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "ScorePlus Game (*.spg)|*.spg|JSON (*.json)|*.json", DefaultExt = "spg", FileName = $"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}" };
        if (dialog.ShowDialog() != true) return;
        SaveGameTo(dialog.FileName);
    }

    /// <summary>Writes the game and remembers that this is now the saved state.</summary>
    bool SaveGameTo(string path)
    {
        try
        {
            _engine.Save(path);
            _projectPath = path;
            _savedStateJson = _engine.ExportJson();
            StatusText.Text = $"GAME SAVED · {System.IO.Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"COULD NOT SAVE GAME · {ex.Message}";
            return false;
        }
    }

    bool HasUnsavedChanges() => _savedStateJson is not null && _savedStateJson != _engine.ExportJson();

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

    string GameLabelForPrompt()
    {
        var s = _engine.State;
        var away = string.IsNullOrWhiteSpace(s.AwayAbbreviation) ? s.AwayTeam : s.AwayAbbreviation;
        var home = string.IsNullOrWhiteSpace(s.HomeAbbreviation) ? s.HomeTeam : s.HomeAbbreviation;
        return $"{away} {s.AwayScore}   ·   {s.HomeScore} {home}";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (PromptOnExit && !_closingAfterPrompt && HasUnsavedChanges())
        {
            e.Cancel = true;
            var detail = "You have changes that are not in a saved game file. Autosave keeps a recovery point every few seconds, but the .spg on disk is out of date. Save it before closing?";
            var choice = UnsavedChangesWindow.Ask(this, _preferences.Theme, GameLabelForPrompt(), detail);
            if (choice == UnsavedChoice.Cancel) return;
            if (choice == UnsavedChoice.Save && !SaveBeforeClosing()) return;
            _closingAfterPrompt = true;
            // WPF forbids Close/ShowDialog while the closing event is still running. Re-post the
            // close once this cancellation has fully finished, so Discard really exits.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
            return;
        }
        base.OnClosing(e);
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
            AutosaveStore.Save(_engine, _startNewAutosavePoint);
            _startNewAutosavePoint = false;
            AutosaveText.Text = $"AUTOSAVED {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { StatusText.Text = $"AUTOSAVE WARNING · {ex.Message}"; }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_panel.TryHandleKey(key, Keyboard.Modifiers)) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine.Changed -= Refresh;
        _engine.Changed -= EngineChanged;
        _preview?.Dispose();
        _session.Dispose();
        foreach (var panel in _controlPanels.ToArray()) panel.Close();
        foreach (var output in _outputs.ToArray()) output.Close();
        _log?.Close();
        base.OnClosed(e);
    }
}
