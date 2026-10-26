using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public partial class GameControlWindow : Window
{
    readonly GameEngine _engine;
    readonly Action _publishOutput;
    readonly Action<bool> _setLiveScoreClock;
    readonly Func<bool> _getLiveScoreClock;
    KeybindSettings _keybinds;
    bool _loadingFormats;
    bool _syncingLive;
    string _statsSignature = "";
    readonly Dictionary<string, TextBlock> _statValues = [];

    sealed record PeriodFormatOption(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    static readonly PeriodFormatOption[] PeriodFormats =
    [
        new("Full", "Quarter 1"),
        new("Short", "Q1"),
        new("Number", "1")
    ];

    public event Action? NewPanelRequested;

    public GameControlWindow(GameEngine engine, Action publishOutput, Action<bool> setLiveScoreClock, Func<bool> getLiveScoreClock)
    {
        InitializeComponent();
        _engine = engine;
        _publishOutput = publishOutput;
        _setLiveScoreClock = setLiveScoreClock;
        _getLiveScoreClock = getLiveScoreClock;
        _keybinds = KeybindStore.Load();
        PeriodFormatSelector.ItemsSource = PeriodFormats;
        SyncLiveState(_getLiveScoreClock());
        _engine.Changed += Refresh;
        Refresh();
        Loaded += (_, _) => ApplyKeybindHints();
        ApplyKeybindHints();
    }

    public void SyncLiveState(bool live)
    {
        if (LiveOutputToggle is null || LiveOutputStatus is null) return;
        _syncingLive = true;
        LiveOutputToggle.IsChecked = live;
        LiveOutputToggle.Content = live ? "Live score & clock: ON" : "Live score & clock: OFF";
        LiveOutputStatus.Text = StatusLine.Text = live ? "LIVE SCORE/CLOCK" : "MANUAL OUTPUT";
        _syncingLive = false;
    }

    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var s = _engine.State;
        HomeTitle.Text = s.HomeTeam; AwayTitle.Text = s.AwayTeam;
        HomeScore.Text = s.HomeScore.ToString(); AwayScore.Text = s.AwayScore.ToString();
        HomeFouls.Text = $"Fouls {s.HomeFouls} · Timeouts {s.HomeTimeouts}";
        AwayFouls.Text = $"Fouls {s.AwayFouls} · Timeouts {s.AwayTimeouts}";
        Clock.Text = GameEngine.FormatClock(s.ClockSeconds);
        ClockToggle.Content = s.ClockRunning ? "Stop clock" : "Start clock";
        var football = s.SportId == "football";
        HomeTouchdownScore.Visibility = AwayTouchdownScore.Visibility = HomeTouchdownSubtract.Visibility = AwayTouchdownSubtract.Visibility = football ? Visibility.Visible : Visibility.Collapsed;
        PeriodLabel.Text = $"{PeriodDisplay.Format(s)} / {s.TotalPeriods}";
        FootballPanel.Visibility = football ? Visibility.Visible : Visibility.Collapsed;
        UpdateScoreGeneral.Visibility = football ? Visibility.Collapsed : Visibility.Visible;
        SoccerPanel.Visibility = s.SportId == "soccer" ? Visibility.Visible : Visibility.Collapsed;
        BaseballPanel.Visibility = s.SportId == "baseball" ? Visibility.Visible : Visibility.Collapsed;
        BasketballPanel.Visibility = s.SportId == "basketball" ? Visibility.Visible : Visibility.Collapsed;
        CountDisplay.Text = $"{s.Balls} balls · {s.Strikes} strikes · {s.Outs} outs";
        HitsDisplay.Text = $"Hits · {s.HomeTeam}: {s.HomeHits}   {s.AwayTeam}: {s.AwayHits}";
        DownDisplay.Text = $"{Ordinal(s.Down)} & {s.YardsToGo}";
        BallDisplay.Text = $"Ball spot: {s.YardsToGoal} yard line";
        CardsDisplay.Text = $"Home  🟨 {s.HomeYellowCards}   🟥 {s.HomeRedCards}       Away  🟨 {s.AwayYellowCards}   🟥 {s.AwayRedCards}";
        BasketballStatsDisplay.Text = $"Fouls {s.HomeTeam}: {s.HomeFouls} · {s.AwayTeam}: {s.AwayFouls}   Shot clock {s.ShotClockSeconds}s";
        FootballStatsDisplay.Text = $"Yards {s.HomeTeam}: {s.HomeYards} · {s.AwayTeam}: {s.AwayYards}   Turnovers {s.HomeTurnovers}–{s.AwayTurnovers}";
        SoccerStatsDisplay.Text = $"Shots {s.HomeShots}–{s.AwayShots}   Corners {s.HomeCorners}–{s.AwayCorners}";
        _loadingFormats = true;
        PeriodFormatSelector.SelectedItem = PeriodFormats.FirstOrDefault(f => f.Key == s.QuarterDisplayFormat) ?? PeriodFormats[0];
        _loadingFormats = false;
        RefreshStats();
    }

    void RefreshStats()
    {
        var stats = _engine.State.Stats;
        var signature = string.Join("|", stats.Select(s => $"{s.Id}:{s.Name}"));
        if (signature != _statsSignature)
        {
            _statsSignature = signature;
            StatsHost.Children.Clear();
            _statValues.Clear();
            foreach (var stat in stats) StatsHost.Children.Add(BuildStatRow(stat));
        }
        StatsEmpty.Visibility = stats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var stat in stats)
            if (_statValues.TryGetValue(stat.Id, out var value)) value.Text = stat.Value.ToString();
    }

    FrameworkElement BuildStatRow(GameStat stat)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock { Text = stat.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        Button Make(string content, string tooltip, Action click)
        {
            var button = new Button { Content = content, ToolTip = tooltip, Style = (Style)FindResource("StatButton") };
            button.Click += (_, _) => click();
            return button;
        }

        var minus = Make("−", $"{stat.Name} −1", () => _engine.AdjustStat(stat.Id, -1));
        var value = new TextBlock { Text = stat.Value.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 16, FontWeight = FontWeights.Bold };
        var plus = Make("＋", $"{stat.Name} +1", () => _engine.AdjustStat(stat.Id, 1));
        var remove = Make("×", $"Remove {stat.Name}", () => _engine.RemoveStat(stat.Id));

        Grid.SetColumn(minus, 1); grid.Children.Add(minus);
        Grid.SetColumn(value, 2); grid.Children.Add(value);
        Grid.SetColumn(plus, 3); grid.Children.Add(plus);
        Grid.SetColumn(remove, 4); grid.Children.Add(remove);

        _statValues[stat.Id] = value;
        return grid;
    }

    void AddStat_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddStatDialog(_engine.State.Stats.Select(s => s.Name).ToArray()) { Owner = this };
        ThemeManager.Apply(dialog, PreferencesStore.Load().Theme);
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.StatName)) _engine.AddStat(dialog.StatName);
    }

    void LiveOutput_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingLive || LiveOutputToggle is null || LiveOutputStatus is null) return;
        var enabled = LiveOutputToggle.IsChecked == true;
        LiveOutputToggle.Content = enabled ? "Live score & clock: ON" : "Live score & clock: OFF";
        LiveOutputStatus.Text = StatusLine.Text = enabled ? "LIVE SCORE/CLOCK" : "MANUAL OUTPUT";
        _setLiveScoreClock(enabled);
    }

    void PeriodFormat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingFormats || PeriodFormatSelector.SelectedItem is not PeriodFormatOption option) return;
        _engine.SetPeriodFormat(option.Key);
    }

    void NewPanel_Click(object sender, RoutedEventArgs e) => NewPanelRequested?.Invoke();

    void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string actionId }) Execute(actionId);
    }

    public void Execute(string actionId)
    {
        switch (actionId)
        {
            case "home.score.plus1": _engine.Score(true, 1); break;
            case "home.score.plus2": _engine.Score(true, 2); break;
            case "home.score.plus3": _engine.Score(true, 3); break;
            case "home.score.plus6": _engine.Score(true, 6); break;
            case "home.score.minus1": _engine.Score(true, -1); break;
            case "home.score.minus2": _engine.Score(true, -2); break;
            case "home.score.minus3": _engine.Score(true, -3); break;
            case "home.score.minus6": _engine.Score(true, -6); break;
            case "away.score.plus1": _engine.Score(false, 1); break;
            case "away.score.plus2": _engine.Score(false, 2); break;
            case "away.score.plus3": _engine.Score(false, 3); break;
            case "away.score.plus6": _engine.Score(false, 6); break;
            case "away.score.minus1": _engine.Score(false, -1); break;
            case "away.score.minus2": _engine.Score(false, -2); break;
            case "away.score.minus3": _engine.Score(false, -3); break;
            case "away.score.minus6": _engine.Score(false, -6); break;
            case "home.timeout": _engine.Timeout(true); break;
            case "away.timeout": _engine.Timeout(false); break;
            case "possession.toggle": _engine.TogglePossession(); break;
            case "clock.toggle": _engine.ToggleClock(); break;
            case "clock.minus60": _engine.AdjustClock(-60); break;
            case "clock.plus60": _engine.AdjustClock(60); break;
            case "clock.minus1": _engine.AdjustClock(-1); break;
            case "clock.plus1": _engine.AdjustClock(1); break;
            case "clock.set": SetClock(); break;
            case "period.next": _engine.NextPeriod(); break;
            case "football.down.next": NextDown(); break;
            case "football.down.previous": _engine.PreviousDown(); break;
            case "football.distance.minus": _engine.SetFootballDistance(_engine.State.YardsToGo - 1); break;
            case "football.distance.plus": _engine.SetFootballDistance(_engine.State.YardsToGo + 1); break;
            case "football.ball.minus": _engine.SetFootballDistance(_engine.State.YardsToGo, Math.Min(99, _engine.State.YardsToGoal + 1)); break;
            case "football.ball.plus": _engine.SetFootballDistance(_engine.State.YardsToGo, Math.Max(0, _engine.State.YardsToGoal - 1)); break;
            case "football.series.new": _engine.NewSeries(); break;
            case "football.yards.home": _engine.AddFootballYards(true, 10); break;
            case "football.yards.away": _engine.AddFootballYards(false, 10); break;
            case "football.turnover.home": _engine.AddTurnover(true); break;
            case "football.turnover.away": _engine.AddTurnover(false); break;
            case "output.publish": _publishOutput(); break;
            case "basketball.foul.home": _engine.AddFoul(true); break;
            case "basketball.foul.away": _engine.AddFoul(false); break;
            case "basketball.shotclock.toggle": _engine.ToggleShotClock(); break;
            case "basketball.shotclock.reset": _engine.ResetShotClock(); break;
            case "basketball.shotclock.set": ShotClock(); break;
            case "baseball.ball": _engine.BaseballBall(); break;
            case "baseball.strike": _engine.BaseballStrike(); break;
            case "baseball.foul": _engine.BaseballStrike(true); break;
            case "baseball.out": _engine.BaseballOut(); break;
            case "baseball.hit.home": _engine.BaseballHit(true); break;
            case "baseball.hit.away": _engine.BaseballHit(false); break;
            case "soccer.yellow.home": _engine.AddCard(true); break;
            case "soccer.yellow.away": _engine.AddCard(false); break;
            case "soccer.red.home": _engine.AddCard(true, true); break;
            case "soccer.red.away": _engine.AddCard(false, true); break;
            case "soccer.shot.home": _engine.AddSoccerShot(true); break;
            case "soccer.shot.away": _engine.AddSoccerShot(false); break;
            case "soccer.corner.home": _engine.AddSoccerCorner(true); break;
            case "soccer.corner.away": _engine.AddSoccerCorner(false); break;
            case "global.undo": _engine.Undo(); break;
        }
    }

    void NextDown()
    {
        if (_engine.State.SportId != "football") return;
        if (_engine.State.Down == 4 && MessageBox.Show(this, "Fourth down: start a new series and reset distance?", "New series", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _engine.AdvanceDown();
    }

    void SetClock()
    {
        var dialog = new ClockTimeDialog("Set game clock", _engine.State.ClockSeconds) { Owner = this };
        if (dialog.ShowDialog() == true) _engine.SetClock(dialog.Seconds);
    }

    void ShotClock()
    {
        var dialog = new ClockTimeDialog("Set shot clock", _engine.State.ShotClockSeconds) { Owner = this };
        if (dialog.ShowDialog() == true) _engine.SetShotClock(dialog.Seconds);
    }

    void Keybinds_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new KeybindsWindow(_keybinds) { Owner = this };
        ThemeManager.Apply(dialog, PreferencesStore.Load().Theme);
        if (dialog.ShowDialog() == true)
        {
            ApplyKeybinds(dialog.Result);
            KeybindStore.Save(_keybinds);
        }
    }

    public void ApplyKeybinds(KeybindSettings settings)
    {
        _keybinds = settings;
        ApplyKeybindHints();
    }

    void ApplyKeybindHints()
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (actionId, gesture) in _keybinds.Bindings) hints[actionId] = gesture;
        foreach (var button in FindTaggedButtons(this))
        {
            var id = button.Tag as string ?? "";
            var label = ControlActions.LabelFor(id);
            button.ToolTip = hints.TryGetValue(id, out var gesture) ? $"{label}  ·  {gesture}" : label;
        }
    }

    static IEnumerable<Button> FindTaggedButtons(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && button.Tag is string) yield return button;
            foreach (var nested in FindTaggedButtons(child)) yield return nested;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) { base.OnPreviewKeyDown(e); return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        var actionId = KeybindStore.Match(_keybinds, key, Keyboard.Modifiers);
        if (actionId is not null)
        {
            Execute(actionId);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        SyncLiveState(_getLiveScoreClock());
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine.Changed -= Refresh;
        base.OnClosed(e);
    }

    static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };
}

internal sealed class AddStatDialog : Window
{
    readonly ComboBox _presets = new();
    readonly TextBox _custom = new();

    public string StatName { get; private set; } = "";

    public AddStatDialog(IReadOnlyCollection<string> existing)
    {
        Title = "Add custom stat";
        Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Choose a common stat or type your own", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 0, 0, 6) });
        _presets.ItemsSource = CommonStats.Names.Where(n => !existing.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray();
        _presets.SelectedIndex = 0;
        panel.Children.Add(_presets);
        panel.Children.Add(new TextBlock { Text = "Custom name (overrides the choice above)", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 8, 0, 6) });
        panel.Children.Add(_custom);
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 114)), Margin = new Thickness(4, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var add = new Button { Content = "Add stat", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        add.Click += (_, _) =>
        {
            var name = _custom.Text.Trim();
            if (name.Length == 0) name = (_presets.SelectedItem as string ?? "").Trim();
            if (name.Length == 0) { error.Text = "Choose a stat or type a name."; return; }
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) { error.Text = "That stat already exists."; return; }
            StatName = name;
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(add); buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _custom.Focus();
    }
}

internal sealed class ClockTimeDialog : Window
{
    readonly TextBox _input = new();
    readonly TextBlock _error = new();
    public int Seconds { get; private set; }

    public ClockTimeDialog(string title, int seconds)
    {
        Title = title;
        Width = 360; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;
        Seconds = seconds;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Enter MM:SS (for example 10:30) or a total number of seconds.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 0, 0, 8) });
        _input.Text = ClockFormat.Format(seconds);
        panel.Children.Add(_input);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 114));
        _error.Margin = new Thickness(4, 4, 0, 0);
        _error.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_error);
        var apply = new Button { Content = "Apply", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        apply.Click += (_, _) =>
        {
            if (!ClockFormat.TryParse(_input.Text, out var parsed)) { _error.Text = "Use MM:SS or a whole number of seconds."; return; }
            Seconds = parsed;
            DialogResult = true;
            Close();
        };
        panel.Children.Add(apply);
        Content = panel;
        ThemeManager.Apply(this, PreferencesStore.Load().Theme);
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }


}
