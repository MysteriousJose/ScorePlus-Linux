using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>What the control surface needs from whatever window hosts it.</summary>
public interface IControlPanelHost
{
    void PublishOutput();
    bool LiveScoreClock { get; set; }
    bool HasStagedChanges { get; }
    void OpenNewPanel();

    /// <summary>"1 · Broadcast Classic" — the layout the preview is showing and would publish.</summary>
    string LayoutLabel { get; }

    /// <summary>Moves the previewed layout forward or back; Update Output puts it on air.</summary>
    void ShiftLayout(int delta);
}

/// <summary>
/// The whole operator surface: team panels, down &amp; distance, clocks, timeouts, possession,
/// and custom stats. Used full-size in the main window and inside a popped-out panel, so both
/// always behave identically.
/// </summary>
public partial class ControlPanelView : UserControl
{
    static readonly Brush StagedBrush = Freeze(Color.FromRgb(255, 209, 102));
    static readonly Brush PublishedBrush = Freeze(Color.FromRgb(56, 215, 163));
    static Brush Freeze(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    readonly GameEngine _engine;
    readonly IControlPanelHost _host;
    readonly Stepper _awayScore, _homeScore, _down, _toGo, _ballOn, _awayTimeouts, _homeTimeouts;
    readonly TextBox _quarterValue = new();
    readonly Button _ballSide;
    readonly Dictionary<string, Stepper> _statValues = [];
    string? _awayLogoLoaded;
    string? _homeLogoLoaded;
    string _scoreSignature = "";
    string _sportSignature = "";
    string _statsSignature = "";
    bool _syncingLive;
    bool _loadingNames;
    bool _showHints;
    KeybindSettings _keybinds = KeybindStore.Load();

    public ControlPanelView(GameEngine engine, IControlPanelHost host)
    {
        InitializeComponent();
        _engine = engine;
        _host = host;

        _awayScore = BuildScoreStepper(false);
        _homeScore = BuildScoreStepper(true);
        AwayScoreHost.Content = _awayScore;
        HomeScoreHost.Content = _homeScore;

        _down = new Stepper("Down", 26);
        _down.SetButtons([new("+", 1, "football.down.next")], [new("−", -1, "football.down.previous")]);
        _down.Stepped += Step;
        _down.Committed += value => _engine.SetDown(value);
        DownHost.Content = _down;

        _toGo = new Stepper("To go", 26);
        _toGo.SetButtons([new("+", 1, "football.distance.plus")], [new("−", -1, "football.distance.minus")]);
        _toGo.Stepped += Step;
        _toGo.Committed += value => _engine.SetFootballDistance(value);
        ToGoHost.Content = _toGo;

        _ballOn = new Stepper("BALL ON", 26);
        _ballOn.ToolTip = "Ball spot as a yard line, 0–50, on either team's half. Its side is independent of possession.";
        _ballOn.SetButtons([new("+", -1, "football.ball.plus")], [new("−", 1, "football.ball.minus")]);
        _ballOn.Stepped += Step;
        _ballOn.Committed += value => _engine.SetFootballDistance(_engine.State.YardsToGo, value);
        // A full-size console button, keybindable like the rest, naming the half the ball is on.
        _ballSide = new Button
        {
            Content = "HOME ▶",
            Tag = "football.ball.side",
            Style = (Style)FindResource("Tiny"),
            Margin = new Thickness(2, 6, 2, 0),
            FontSize = 11,
            ToolTip = "Which team's half of the field the ball is on — independent of possession"
        };
        _ballSide.Click += Action_Click;
        var ballColumn = new StackPanel();
        ballColumn.Children.Add(_ballOn);
        ballColumn.Children.Add(_ballSide);
        BallOnHost.Content = ballColumn;

        // How the quarter prints (Quarter 1 / Q1 / 1) belongs to the graphic, not the console:
        // it is a property of the element in Scorebug Studio.
        QuarterHost.Content = BuildQuarterControl();

        _awayTimeouts = BuildTimeoutStepper(false);
        _homeTimeouts = BuildTimeoutStepper(true);
        AwayTimeoutsHost.Content = _awayTimeouts;
        HomeTimeoutsHost.Content = _homeTimeouts;

        AwayName.KeyDown += Name_KeyDown;
        HomeName.KeyDown += Name_KeyDown;
        AwayName.LostKeyboardFocus += Name_LostFocus;
        HomeName.LostKeyboardFocus += Name_LostFocus;

        _engine.Changed += Refresh;
        Loaded += (_, _) => { ApplyKeybindHints(); Refresh(); };
        Refresh();
    }

    /// <summary>The steppers the tests and the host inspect.</summary>
    public string HomeScoreText => _homeScore.Text;
    public string AwayScoreText => _awayScore.Text;
    public string PlayClockText => PlayClockValue.Text;
    public Stepper HomeScore => _homeScore;
    public Stepper AwayScore => _awayScore;
    public Stepper BallOn => _ballOn;

    Stepper BuildScoreStepper(bool home)
    {
        var stepper = new Stepper("Score", 38);
        stepper.Stepped += Step;
        stepper.Committed += value => _engine.SetScore(home, value);
        return stepper;
    }

    Stepper BuildTimeoutStepper(bool home)
    {
        var stepper = new Stepper(home ? "Home" : "Visitors", 21);
        stepper.SetButtons([new("+", 1, home ? "home.timeout.plus" : "away.timeout.plus")], [new("−", -1, home ? "home.timeout" : "away.timeout")]);
        stepper.Stepped += Step;
        stepper.Committed += value => _engine.SetTimeoutCount(home, value);
        return stepper;
    }

    /// <summary>A compact − / quarter / + row, built from the same full-size console buttons.</summary>
    FrameworkElement BuildQuarterControl()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock { Text = "QUARTER", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        caption.Style = (Style)FindResource("Caption");
        grid.Children.Add(caption);
        _quarterValue.FontFamily = new FontFamily("Consolas");
        _quarterValue.FontSize = 22;
        _quarterValue.FontWeight = FontWeights.Bold;
        _quarterValue.TextAlignment = TextAlignment.Center;
        _quarterValue.Background = Brushes.Transparent;
        _quarterValue.BorderThickness = new Thickness(0);
        _quarterValue.Margin = new Thickness(4, 0, 4, 0);
        _quarterValue.ToolTip = "Type a period number and press Enter";
        _quarterValue.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (int.TryParse(_quarterValue.Text.Trim(), out var period)) _engine.SetPeriod(period);
            else _quarterValue.Text = _engine.State.Period.ToString();
            Keyboard.ClearFocus();
            e.Handled = true;
        };
        _quarterValue.LostKeyboardFocus += (_, _) => _quarterValue.Text = _engine.State.Period.ToString();
        Grid.SetColumn(_quarterValue, 1);
        grid.Children.Add(_quarterValue);
        var minus = new Button { Content = "−", Tag = "period.previous", Style = (Style)FindResource("Tiny"), MinWidth = 44 };
        minus.Click += Action_Click;
        Grid.SetColumn(minus, 2);
        grid.Children.Add(minus);
        var plus = new Button { Content = "+", Tag = "period.next", Style = (Style)FindResource("Tiny"), MinWidth = 44 };
        plus.Click += Action_Click;
        Grid.SetColumn(plus, 3);
        grid.Children.Add(plus);
        return grid;
    }

    /// <summary>Adds/subtracts through the action table so keybinds and buttons share one path.</summary>
    void Step(Stepper.StepButton button)
    {
        if (button.ActionId is not null) Execute(button.ActionId);
        else if (button.Delta != 0) _engine.Score(false, button.Delta);
    }

    /// <summary>Small explanatory tips next to headings; off by default (Preferences → Show heading tips).</summary>
    public void SetHintVisibility(bool show)
    {
        _showHints = show;
        DetailCardHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SyncLiveState(bool live)
    {
        if (LiveOutputToggle is null) return;
        _syncingLive = true;
        LiveOutputToggle.IsChecked = live;
        LiveOutputToggle.Content = live ? "Live: ON" : "Live: OFF";
        _syncingLive = false;
    }

    public void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var s = _engine.State;

        _loadingNames = true;
        if (!AwayName.IsKeyboardFocused) AwayName.Text = s.AwayTeam;
        if (!HomeName.IsKeyboardFocused) HomeName.Text = s.HomeTeam;
        _loadingNames = false;

        RebuildScoreButtons(s);
        _awayScore.Value = s.AwayScore;
        _homeScore.Value = s.HomeScore;
        _awayTimeouts.Value = s.AwayTimeouts;
        _homeTimeouts.Value = s.HomeTimeouts;
        _down.Value = s.Down;
        _toGo.Value = s.YardsToGo;
        _ballOn.Value = s.YardsToGoal;
        _ballSide.Content = s.BallOnHomeSide ? "HOME ▶" : "◀ VISITORS";
        if (!_quarterValue.IsKeyboardFocused) _quarterValue.Text = s.Period.ToString();

        // Team identity: abbreviation, logo thumbnail and the two-colour bar under the name.
        AwayAbbrText.Text = s.AwayAbbreviation;
        HomeAbbrText.Text = s.HomeAbbreviation;
        ApplyLogo(AwayLogoImage, ref _awayLogoLoaded, s.AwayLogoPath);
        ApplyLogo(HomeLogoImage, ref _homeLogoLoaded, s.HomeLogoPath);
        AwayColorBar.Background = TeamBar(s.AwayPrimaryColor, s.AwaySecondaryColor);
        HomeColorBar.Background = TeamBar(s.HomePrimaryColor, s.HomeSecondaryColor);
        LayoutLabel.Text = _host.LayoutLabel;

        if (!ClockValue.IsKeyboardFocused) ClockValue.Text = GameEngine.FormatClock(s.ClockSeconds);
        if (!PlayClockValue.IsKeyboardFocused) PlayClockValue.Text = s.PlayClockSeconds.ToString();
        ShotClockValue.Text = s.ShotClockSeconds.ToString();
        TimeoutClockValue.Text = GameEngine.FormatClock(s.TimeoutClockSeconds);
        ClockToggle.Content = s.ClockRunning ? "Stop" : "Start";
        PlayClockToggle.Content = s.PlayClockRunning ? "Stop" : "Start";
        ShotClockToggle.Content = s.ShotClockRunning ? "Stop" : "Start";
        TimeoutClockToggle.Content = s.TimeoutClockRunning ? "Stop" : "Start";

        PossessionValue.Text = s.HomePossession ? s.HomeTeam : s.AwayTeam;
        AwayPossessionTag.Text = s.HomePossession ? "" : "◀ POSSESSION";
        HomePossessionTag.Text = s.HomePossession ? "POSSESSION ▶" : "";
        AwayPossessionButton.FontWeight = s.HomePossession ? FontWeights.Normal : FontWeights.Bold;
        HomePossessionButton.FontWeight = s.HomePossession ? FontWeights.Bold : FontWeights.Normal;

        (AwayMeta.Text, HomeMeta.Text) = s.SportId switch
        {
            "football" => ($"Yards {s.AwayYards} · Turnovers {s.AwayTurnovers}", $"Yards {s.HomeYards} · Turnovers {s.HomeTurnovers}"),
            "basketball" => ($"Fouls {s.AwayFouls}", $"Fouls {s.HomeFouls}"),
            "soccer" => ($"🟨 {s.AwayYellowCards}  🟥 {s.AwayRedCards}  ⚽ {s.AwayShots}  ◆ {s.AwayCorners}",
                         $"🟨 {s.HomeYellowCards}  🟥 {s.HomeRedCards}  ⚽ {s.HomeShots}  ◆ {s.HomeCorners}"),
            "baseball" => ($"Hits {s.AwayHits}", $"Hits {s.HomeHits}"),
            _ => ($"Timeouts {s.AwayTimeouts}", $"Timeouts {s.HomeTimeouts}")
        };

        var football = s.SportId == "football";
        FootballDetail.Visibility = football ? Visibility.Visible : Visibility.Collapsed;
        OtherDetail.Visibility = football ? Visibility.Collapsed : Visibility.Visible;
        NewSeriesButton.Visibility = FootballExtras.Visibility = football ? Visibility.Visible : Visibility.Collapsed;
        DetailCardTitle.Text = football ? "DOWN & DISTANCE" : "SPORT CONTROLS";
        DetailCardHint.Text = football ? $"{Ordinal(s.Down)} & {s.YardsToGo} · ball on {(s.BallOnHomeSide ? "home" : "visitors")}' {s.YardsToGoal}" : string.Empty;
        DetailCardHint.Visibility = _showHints && football ? Visibility.Visible : Visibility.Collapsed;
        PlayClockRow.Visibility = football ? Visibility.Visible : Visibility.Collapsed;
        // Football has a play clock; showing a shot clock there as well only crowded the page.
        ShotClockRow.Visibility = _engine.Sport.HasShotClock && !football ? Visibility.Visible : Visibility.Collapsed;
        RebuildSportButtons(s);

        var staged = _host.HasStagedChanges;
        StagedText.Text = staged ? "●  NOT ON AIR YET — press Update Output" : "EVERYTHING ON AIR";
        StagedText.Foreground = staged ? StagedBrush : PublishedBrush;
        UpdateButton.FontWeight = staged ? FontWeights.Bold : FontWeights.SemiBold;
        SyncLiveState(_host.LiveScoreClock);

        RefreshStats();
    }

    /// <summary>Loads a team logo once per path; the console refreshes every clock tick.</summary>
    void ApplyLogo(Image target, ref string? cache, string? path)
    {
        var wanted = File.Exists(path) ? path : null;
        if (string.Equals(cache, wanted)) return;
        cache = wanted;
        if (wanted is null) { target.Source = null; return; }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(wanted);
            image.EndInit();
            image.Freeze();
            target.Source = image;
        }
        catch { target.Source = null; }
    }

    static Brush TeamBar(string primary, string secondary)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        brush.GradientStops.Add(new GradientStop(SafeColor(primary), 0));
        brush.GradientStops.Add(new GradientStop(SafeColor(primary), 0.55));
        brush.GradientStops.Add(new GradientStop(SafeColor(secondary), 0.55));
        brush.GradientStops.Add(new GradientStop(SafeColor(secondary), 1));
        brush.Freeze();
        return brush;
    }

    static Color SafeColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value)!; }
        catch { return Color.FromRgb(90, 100, 112); }
    }

    void AwayDetails_Click(object sender, RoutedEventArgs e) => EditTeam(false);
    void HomeDetails_Click(object sender, RoutedEventArgs e) => EditTeam(true);

    void EditTeam(bool home)
    {
        var dialog = new TeamIdentityWindow(_engine, home, PreferencesStore.Load().Theme) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) Refresh();
    }

    void LayoutPrev_Click(object sender, RoutedEventArgs e) => ShiftLayout(-1);
    void LayoutNext_Click(object sender, RoutedEventArgs e) => ShiftLayout(1);

    void ShiftLayout(int delta)
    {
        _host.ShiftLayout(delta);
        Refresh();
    }

    void ShotClock14_Click(object sender, RoutedEventArgs e) => _engine.SetShotClock(14);

    void RebuildScoreButtons(GameState state)
    {
        var signature = string.Join(",", state.ScoringOptions);
        if (signature == _scoreSignature) return;
        _scoreSignature = signature;
        var options = state.ScoringOptions.Length == 0 ? [1] : state.ScoringOptions;
        _awayScore.SetButtons(
            options.Select(o => new Stepper.StepButton($"+{o}", o, $"away.score.plus{o}")),
            options.Select(o => new Stepper.StepButton($"−{o}", -o, $"away.score.minus{o}")));
        _homeScore.SetButtons(
            options.Select(o => new Stepper.StepButton($"+{o}", o, $"home.score.plus{o}")),
            options.Select(o => new Stepper.StepButton($"−{o}", -o, $"home.score.minus{o}")));
    }

    void RebuildSportButtons(GameState state)
    {
        var signature = state.SportId;
        if (signature == _sportSignature) return;
        _sportSignature = signature;
        OtherDetail.Children.Clear();
        foreach (var (label, actionId) in SportActions(state.SportId))
        {
            var button = new Button { Content = label, Tag = actionId, FontSize = 11, Padding = new Thickness(6, 6, 6, 6), Margin = new Thickness(2) };
            button.Click += Action_Click;
            OtherDetail.Children.Add(button);
        }
    }

    static IEnumerable<(string Label, string ActionId)> SportActions(string sportId) => sportId switch
    {
        "basketball" =>
        [
            ("Visitors foul", "basketball.foul.away"), ("Home foul", "basketball.foul.home"),
            ("Shot clock ±", "basketball.shotclock.toggle"), ("Reset 24", "basketball.shotclock.reset")
        ],
        "baseball" =>
        [
            ("Ball", "baseball.ball"), ("Strike", "baseball.strike"), ("Foul ball", "baseball.foul"), ("Out", "baseball.out"),
            ("Visitors hit", "baseball.hit.away"), ("Home hit", "baseball.hit.home")
        ],
        "soccer" =>
        [
            ("Visitors 🟨", "soccer.yellow.away"), ("Home 🟨", "soccer.yellow.home"),
            ("Visitors 🟥", "soccer.red.away"), ("Home 🟥", "soccer.red.home"),
            ("Visitors shot", "soccer.shot.away"), ("Home shot", "soccer.shot.home"),
            ("Visitors corner", "soccer.corner.away"), ("Home corner", "soccer.corner.home")
        ],
        _ => []
    };

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
            if (_statValues.TryGetValue(stat.Id, out var value)) value.Value = stat.Value;
    }

    /// <summary>Custom stats use the same add-above / value / subtract-below shape as scores, two per row.</summary>
    FrameworkElement BuildStatRow(GameStat stat)
    {
        var stepper = new Stepper(stat.Name, 18) { Width = 104, Margin = new Thickness(0, 0, 4, 4) };
        stepper.SetButtons
        (
            [new("＋ 1", 1, null, $"{stat.Name} +1")],
            [new("− 1", -1, null, $"{stat.Name} −1")]
        );
        stepper.Stepped += button => _engine.AdjustStat(stat.Id, button.Delta);
        stepper.Committed += value => _engine.SetStat(stat.Id, value);
        stepper.Value = stat.Value;

        var host = new Grid { Width = 110, Margin = new Thickness(0, 0, 4, 2), ToolTip = $"Remove {stat.Name}" };
        host.Children.Add(stepper);
        var remove = new Button
        {
            Content = "×", Width = 16, Height = 16, Padding = new Thickness(0), FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, ToolTip = $"Remove {stat.Name}"
        };
        remove.Click += (_, _) => _engine.RemoveStat(stat.Id);
        host.Children.Add(remove);

        _statValues[stat.Id] = stepper;
        return host;
    }

    void Name_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CommitNames();
        Keyboard.ClearFocus();
    }

    void Name_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitNames();

    void CommitNames()
    {
        if (_loadingNames) return;
        var home = HomeName.Text.Trim();
        var away = AwayName.Text.Trim();
        if (home.Length == 0) home = _engine.State.HomeTeam;
        if (away.Length == 0) away = _engine.State.AwayTeam;
        if (home == _engine.State.HomeTeam && away == _engine.State.AwayTeam) return;
        _engine.SetTeams(home, away);
    }

    void ClockValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ClockFormat.TryParse(ClockValue.Text, out var seconds)) _engine.SetClock(seconds);
            else ClockValue.Text = GameEngine.FormatClock(_engine.State.ClockSeconds);
            ClockValue.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { ClockValue.Text = GameEngine.FormatClock(_engine.State.ClockSeconds); Keyboard.ClearFocus(); e.Handled = true; }
    }

    void ClockValue_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ClockFormat.TryParse(ClockValue.Text, out var seconds)) _engine.SetClock(seconds);
        else ClockValue.Text = GameEngine.FormatClock(_engine.State.ClockSeconds);
    }

    void PlayClock_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitPlayClock();
            PlayClockValue.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { PlayClockValue.Text = _engine.State.PlayClockSeconds.ToString(); Keyboard.ClearFocus(); e.Handled = true; }
    }

    void PlayClock_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitPlayClock();

    void CommitPlayClock()
    {
        if (int.TryParse(PlayClockValue.Text.Trim(), out var seconds)) _engine.SetPlayClock(seconds);
        else PlayClockValue.Text = _engine.State.PlayClockSeconds.ToString();
    }

    void PlayClock40_Click(object sender, RoutedEventArgs e) => _engine.ResetPlayClock(40);
    void PlayClock25_Click(object sender, RoutedEventArgs e) => _engine.ResetPlayClock(25);

    void AddStat_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddStatDialog(_engine.State.Stats.Select(s => s.Name).ToArray()) { Owner = Window.GetWindow(this) };
        ThemeManager.Apply(dialog, PreferencesStore.Load().Theme);
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.StatName)) _engine.AddStat(dialog.StatName);
    }

    void LiveOutput_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingLive || LiveOutputToggle is null || _host is null) return;
        var enabled = LiveOutputToggle.IsChecked == true;
        LiveOutputToggle.Content = enabled ? "Live: ON" : "Live: OFF";
        _host.LiveScoreClock = enabled;
    }

    void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string actionId }) Execute(actionId);
    }

    public void Execute(string actionId)
    {
        switch (actionId)
        {
            case "output.publish": _host.PublishOutput(); break;
            case "output.live.toggle": _host.LiveScoreClock = !_host.LiveScoreClock; break;
            case "possession.away": _engine.SetPossession(false); break;
            case "possession.home": _engine.SetPossession(true); break;
            case "possession.toggle": _engine.TogglePossession(); break;
            case "home.timeout.plus": _engine.AdjustTimeouts(true, 1); break;
            case "away.timeout.plus": _engine.AdjustTimeouts(false, 1); break;
            case "home.timeout": _engine.Timeout(true); break;
            case "away.timeout": _engine.Timeout(false); break;
            case "clock.toggle": _engine.ToggleClock(); break;
            case "clock.minus60": _engine.AdjustClock(-60); break;
            case "clock.plus60": _engine.AdjustClock(60); break;
            case "clock.minus1": _engine.AdjustClock(-1); break;
            case "clock.plus1": _engine.AdjustClock(1); break;
            case "clock.set": SetClockDialog(); break;
            case "period.next": _engine.NextPeriod(); break;
            case "period.previous": _engine.PreviousPeriod(); break;
            case "football.playclock.toggle": _engine.TogglePlayClock(); break;
            case "football.playclock.reset40": _engine.ResetPlayClock(40); break;
            case "football.playclock.reset25": _engine.ResetPlayClock(25); break;
            case "football.playclock.minus": _engine.SetPlayClock(_engine.State.PlayClockSeconds - 1); break;
            case "football.playclock.plus": _engine.SetPlayClock(_engine.State.PlayClockSeconds + 1); break;
            case "football.playclock.set": SetPlayClockDialog(); break;
            case "timeoutclock.toggle": _engine.ToggleTimeoutClock(); break;
            case "timeoutclock.reset": _engine.ResetTimeoutClock(60); break;
            case "timeoutclock.minus10": _engine.SetTimeoutClock(_engine.State.TimeoutClockSeconds - 10); break;
            case "timeoutclock.minus1": _engine.SetTimeoutClock(_engine.State.TimeoutClockSeconds - 1); break;
            case "timeoutclock.plus1": _engine.SetTimeoutClock(_engine.State.TimeoutClockSeconds + 1); break;
            case "timeoutclock.plus10": _engine.SetTimeoutClock(_engine.State.TimeoutClockSeconds + 10); break;
            case "timeoutclock.set": SetTimeoutClockDialog(); break;
            case "football.down.next": NextDown(); break;
            case "football.down.previous": _engine.PreviousDown(); break;
            case "football.distance.minus": _engine.SetFootballDistance(_engine.State.YardsToGo - 1); break;
            case "football.distance.plus": _engine.SetFootballDistance(_engine.State.YardsToGo + 1); break;
            case "football.ball.minus": _engine.SetFootballDistance(_engine.State.YardsToGo, Math.Min(50, _engine.State.YardsToGoal + 1)); break;
            case "football.ball.side": _engine.SetBallSide(!_engine.State.BallOnHomeSide); break;
            case "football.ball.plus": _engine.SetFootballDistance(_engine.State.YardsToGo, Math.Max(0, _engine.State.YardsToGoal - 1)); break;
            case "football.series.new": _engine.NewSeries(); break;
            case "football.yards.home": _engine.AddFootballYards(true, 10); break;
            case "football.yards.away": _engine.AddFootballYards(false, 10); break;
            case "football.turnover.home": _engine.AddTurnover(true); break;
            case "football.turnover.away": _engine.AddTurnover(false); break;
            case "basketball.foul.home": _engine.AddFoul(true); break;
            case "basketball.foul.away": _engine.AddFoul(false); break;
            case "basketball.shotclock.toggle": _engine.ToggleShotClock(); break;
            case "basketball.shotclock.reset": _engine.ResetShotClock(); break;
            case "basketball.shotclock.minus": _engine.SetShotClock(_engine.State.ShotClockSeconds - 1); break;
            case "basketball.shotclock.plus": _engine.SetShotClock(_engine.State.ShotClockSeconds + 1); break;
            case "basketball.shotclock.set": SetShotClockDialog(); break;
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
            default:
                if (TryScoreAction(actionId, out var home, out var amount)) _engine.Score(home, amount);
                break;
        }
    }

    static bool TryScoreAction(string actionId, out bool home, out int amount)
    {
        home = false;
        amount = 0;
        string rest;
        if (actionId.StartsWith("home.score.", StringComparison.Ordinal)) { home = true; rest = actionId["home.score.".Length..]; }
        else if (actionId.StartsWith("away.score.", StringComparison.Ordinal)) rest = actionId["away.score.".Length..];
        else return false;

        var sign = 0;
        if (rest.StartsWith("plus", StringComparison.Ordinal)) { sign = 1; rest = rest[4..]; }
        else if (rest.StartsWith("minus", StringComparison.Ordinal)) { sign = -1; rest = rest[5..]; }
        if (sign == 0 || !int.TryParse(rest, out var value)) return false;
        amount = sign * value;
        return true;
    }

    void NextDown()
    {
        if (_engine.State.SportId != "football") return;
        var owner = Window.GetWindow(this);
        if (_engine.State.Down == 4 && MessageBox.Show(owner, "Fourth down: start a new series and reset distance?", "New series", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _engine.AdvanceDown();
    }

    void SetClockDialog()
    {
        var dialog = new ClockTimeDialog("Set game clock", _engine.State.ClockSeconds) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) _engine.SetClock(dialog.Seconds);
    }

    void SetTimeoutClockDialog()
    {
        var dialog = new ClockTimeDialog("Set timeout clock", _engine.State.TimeoutClockSeconds) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) _engine.SetTimeoutClock(dialog.Seconds);
    }

    void SetPlayClockDialog()
    {
        var dialog = new ClockTimeDialog("Set play clock", _engine.State.PlayClockSeconds) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) _engine.SetPlayClock(dialog.Seconds);
    }

    void SetShotClockDialog()
    {
        var dialog = new ClockTimeDialog("Set shot clock", _engine.State.ShotClockSeconds) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) _engine.SetShotClock(dialog.Seconds);
    }

    public void ShowKeybindsDialog()
    {
        var dialog = new KeybindsWindow(_keybinds) { Owner = Window.GetWindow(this) };
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

    public void ApplyKeybindHints()
    {
        var hints = _keybinds.Bindings;
        foreach (var button in FindTaggedButtons(this))
        {
            var id = (string)button.Tag;
            var label = ControlActions.LabelFor(id);
            button.ToolTip = hints.TryGetValue(id, out var gesture) ? $"{label}  ·  {gesture}" : label;
        }
    }

    /// <summary>Routes a keypress through the bindings. False means the key was not ours.</summary>
    public bool TryHandleKey(Key key, ModifierKeys modifiers)
    {
        if (Keyboard.FocusedElement is TextBox) return false;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return false;
        var actionId = KeybindStore.Match(_keybinds, key, modifiers);
        if (actionId is null) return false;
        Execute(actionId);
        return true;
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

    static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };
}
