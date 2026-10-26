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
    OutputWindow? _output;
    EventLogWindow? _log;
    ScorebugCanvasView? _preview;
    int _eventCount = -1;
    readonly UserPreferences _preferences = PreferencesStore.Load();
    readonly string _recoveryPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scoreboard", "recovery.scoreboard");
    string? _projectPath;
    bool _initializing = true;
    bool _liveScoreClock = true;

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

        _panel = new ControlPanelView(_engine, this);
        ControlHost.Content = _panel;
        _panel.SetHintVisibility(_preferences.ShowHeadingHints);

        MediaControl.Attach(_session);
        MediaControl.ApplyTheme(_preferences.Theme);
        _session.Status += message => StatusText.Text = message;
        ApplyHintVisibility();

        Refresh();
        _initializing = false;

        // No recovery prompt: the autosave is restored silently and reported in the status bar.
        // A launched "New Game" starts clean; "Open Game" loads the chosen file; otherwise the
        // autosave is restored silently and reported in the status bar.
        if (mode == LaunchMode.New)
        {
            _engine.NewGame(string.IsNullOrWhiteSpace(_preferences.LastSport) ? "football" : _preferences.LastSport);
            StatusText.Text = "NEW GAME · set the teams and you are live";
        }
        else if (mode == LaunchMode.Open && !string.IsNullOrWhiteSpace(gamePath))
        {
            try
            {
                _engine.Load(gamePath);
                _projectPath = gamePath;
                RefreshSports();
                SportSelector.SelectedValue = _engine.State.SportId;
                StatusText.Text = $"GAME LOADED · {System.IO.Path.GetFileName(gamePath)}";
            }
            catch (Exception ex) { StatusText.Text = $"COULD NOT OPEN GAME · {ex.Message}"; }
        }
        else if (mode is null && File.Exists(_recoveryPath))
        {
            try
            {
                _engine.Load(_recoveryPath);
                StatusText.Text = "RECOVERED · previous game restored (New Game starts fresh)";
            }
            catch { }
        }

        if (_preferences.SeparateControls) OpenControlPanel();
    }

    // ---------- IControlPanelHost ----------
    public bool LiveScoreClock
    {
        get => _liveScoreClock;
        set => SetLiveScoreClock(value);
    }

    public bool HasStagedChanges => GameEngine.StagedSignature(_engine.State) != GameEngine.StagedSignature(_publishedEngine.State);

    public void OpenNewPanel() => OpenControlPanel();

    public void PublishOutput()
    {
        _publishedEngine.LoadFromState(_engine.CloneState());
        _preview?.Refresh();
        _output?.Publish();
        StatusText.Text = $"OUTPUT UPDATED · {DateTime.Now:HH:mm:ss}";
        _panel.Refresh();
    }

    // ---------- window chrome ----------
    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var s = _engine.State;
        if (SportSelector.SelectedValue is not string selectedId || selectedId != s.SportId) SportSelector.SelectedValue = s.SportId;
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
        _preview?.Rebuild();
    }

    void Preferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_preferences) { Owner = this };
        ThemeManager.Apply(dialog, _preferences.Theme);
        if (dialog.ShowDialog() != true) return; // Cancel discards: nothing is applied or saved.
        ThemeManager.Apply(this, _preferences.Theme);
        MediaControl.ApplyTheme(_preferences.Theme);
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.ApplyTheme(_preferences.Theme);
        _output?.SetBroadcastTheme(_preferences.Theme);
        ApplyHintVisibility();
        _panel.SetHintVisibility(_preferences.ShowHeadingHints);
        if (_preferences.SeparateControls && _controlPanels.Count == 0) OpenControlPanel();
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
        RefreshSports();
        if (_engine.Sports.Find(activeId) is not null) SportSelector.SelectedValue = activeId;
    }

    void Sport_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || SportSelector.SelectedValue is not string id) return;
        _engine.NewGame(id);
        _preferences.LastSport = id;
        try { PreferencesStore.Save(_preferences); } catch { }
        StatusText.Text = $"NEW {_engine.Sport.Name.ToUpperInvariant()} GAME";
    }

    void NewGame_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Start a new game? This resets scores and the game clock.", "New game", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _engine.NewGame(SportSelector.SelectedValue as string ?? "basketball");
        StatusText.Text = "NEW GAME";
    }

    // ---------- output ----------
    void Output_Click(object sender, RoutedEventArgs e)
    {
        if (_output is { IsVisible: true }) { _output.Activate(); return; }
        _output = new OutputWindow(_publishedEngine, _scorebug, _preferences.Theme, _playback);
        _output.Show();
        if (_liveScoreClock) _output.UpdateLiveScoreClock(_engine.State);
        else _output.RefreshDisplay();
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
        if (_output is null || !_output.IsVisible) return;
        _output.UpdateLiveScoreClock(_engine.State);
    }

    void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        Output_Click(sender, e);
        _output?.ToggleFullscreen();
    }

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
        var dialog = new SaveFileDialog { Filter = "Scoreboard Game (*.scoreboard)|*.scoreboard|JSON (*.json)|*.json", DefaultExt = "scoreboard", FileName = $"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}" };
        if (dialog.ShowDialog() != true) return;
        _engine.Save(dialog.FileName);
        _projectPath = dialog.FileName;
        StatusText.Text = "GAME SAVED";
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Scoreboard Game (*.scoreboard;*.json)|*.scoreboard;*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _engine.Load(dialog.FileName);
            _projectPath = dialog.FileName;
            RefreshSports();
            SportSelector.SelectedValue = _engine.State.SportId;
            StatusText.Text = "GAME LOADED";
        }
        catch (Exception ex) { MessageBox.Show($"Could not load game: {ex.Message}"); }
    }

    // ---------- lifecycle ----------
    void AutoSave()
    {
        try { _engine.Save(_recoveryPath); AutosaveText.Text = $"AUTOSAVED {DateTime.Now:HH:mm:ss}"; }
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
        _output?.Close();
        _log?.Close();
        base.OnClosed(e);
    }
}
