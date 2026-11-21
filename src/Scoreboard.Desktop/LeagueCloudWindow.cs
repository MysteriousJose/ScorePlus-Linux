using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The league-cloud setup desk: endpoints, polling, received games, selective import, and a
/// ready-made lower-third layout bound to the live cloud feed.
/// </summary>
public sealed class LeagueCloudWindow : Avalonia.Controls.Window
{
    readonly LeagueCloudService _service;
    readonly UserPreferences _preferences;
    readonly ScorebugCatalog _catalog;
    readonly ProgramSelection _program;
    readonly GameEngine _engine;
    readonly TextBox _upload = new();
    readonly TextBox _feed = new();
    readonly TextBox _league = new();
    readonly TextBox _poll = new();
    readonly TextBox _key = new();
    readonly CheckBox _enabled = new() { Content = "Publish this game and fetch the league feed automatically" };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) };
    readonly ListBox _games = new() { MinHeight = 120, MaxHeight = 190, FontSize = 11 };
    readonly CheckBox _importHome = new() { Content = "Home score" };
    readonly CheckBox _importAway = new() { Content = "Away score" };
    readonly CheckBox _importClock = new() { Content = "Game clock" };
    readonly CheckBox _importPeriod = new() { Content = "Period" };
    List<LeagueGame> _shownGames = [];

    public LeagueCloudWindow(LeagueCloudService service, UserPreferences preferences, ScorebugCatalog catalog,
        ProgramSelection program, GameEngine engine)
    {
        _service = service;
        _preferences = preferences;
        _catalog = catalog;
        _program = program;
        _engine = engine;
        Title = "ScorePlus — League Cloud";
        Width = 640;
        Height = 640;
        MinWidth = 540;
        MinHeight = 520;
        ScreenFit.FitDialog(this);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        Content = Build();
        Fill();
        _service.Status += OnStatus;
        _service.GamesChanged += OnGamesChanged;
        Closed += (_, _) =>
        {
            _service.Status -= OnStatus;
            _service.GamesChanged -= OnGamesChanged;
        };
        ThemeManager.Apply(this, preferences.Theme);
        RefreshGames();
        _status.Text = preferences.LeagueCloudEnabled
            ? "League cloud is on. Changes upload after a short delay; the feed refreshes on the interval below."
            : "League cloud is off. Configure an upload URL, a feed URL, or both.";
    }

    Control Build()
    {
        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "LEAGUE CLOUD", FontSize = 18, FontWeight = FontWeight.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "Upload the live game to a league server and fetch the rest of the league for a ticker or lower third.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.75,
            Margin = new Thickness(0, 4, 0, 10)
        });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save", IsDefault = true, MinHeight = 34, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinHeight = 34, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        save.Click += (_, _) => { if (Save()) Close(); };
        close.Click += (_, _) => Close();
        footer.Children.Add(save);
        footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var panel = new StackPanel();
        panel.Children.Add(_enabled);
        panel.Children.Add(Help("The snapshot includes game name, sport, teams, score, period, clock and records. The API key stays in this computer's local preferences."));
        panel.Children.Add(Label("Upload endpoint (POST)"));
        AddBox(panel, _upload, "https://league.example.com/api/games (receives the current game JSON)");
        panel.Children.Add(Label("League feed endpoint (GET)"));
        AddBox(panel, _feed, "https://league.example.com/api/games?league={league} (array, or { games: [...] })");

        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
        left.Children.Add(Label("League id"));
        left.Children.Add(_league);
        left.Children.Add(Help("Sent as the league field and added to the feed URL when it has no {league} placeholder."));
        var right = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
        right.Children.Add(Label("Poll every (seconds)"));
        right.Children.Add(_poll);
        right.Children.Add(Help("5–300 seconds. Local changes also upload shortly after they happen."));
        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        panel.Children.Add(row);

        panel.Children.Add(Label("API key / bearer token (optional)"));
        _key.MinHeight = 32;
        _key.Padding = new Thickness(8, 4, 8, 4);
        _key.PasswordChar = '•';
        panel.Children.Add(_key);
        panel.Children.Add(Help("Use a private HTTPS endpoint. Do not put a key in a public layout or share the preferences file."));

        panel.Children.Add(Label("League games received"));
        panel.Children.Add(_games);
        panel.Children.Add(Help("Pick a game, tick only the values you want, then import. Every value you leave unticked keeps the scoreboard's current value."));
        var importRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var box in new[] { _importHome, _importAway, _importClock, _importPeriod })
        {
            box.Margin = new Thickness(0, 0, 14, 4);
            box.FontSize = 11;
            importRow.Children.Add(box);
        }
        panel.Children.Add(importRow);
        var import = new Button { Content = "Import selected fields", MinHeight = 30, Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        import.Click += (_, _) => ImportSelected();
        panel.Children.Add(import);
        panel.Children.Add(_status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var upload = new Button { Content = "Upload now", MinHeight = 32, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
        var fetch = new Button { Content = "Fetch now", MinHeight = 32, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
        var ticker = new Button { Content = "Add league ticker layout", MinHeight = 32, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
        var rotation = new Button { Content = "Ticker rotation…", MinHeight = 32, Padding = new Thickness(12, 5, 12, 5) };
        upload.Click += async (_, _) => { if (Save()) { _status.Text = "Uploading…"; await _service.UploadNowAsync(); } };
        fetch.Click += async (_, _) => { if (Save()) { _status.Text = "Fetching…"; await _service.FetchNowAsync(); } };
        ticker.Click += (_, _) => { if (Save()) AddTicker(); };
        rotation.Click += (_, _) =>
        {
            if (!Save()) return;
            _ = new TickerWindow(_engine, _preferences.Theme).ShowDialog(this);
        };
        actions.Children.Add(upload);
        actions.Children.Add(fetch);
        actions.Children.Add(ticker);
        actions.Children.Add(rotation);
        panel.Children.Add(actions);

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8, 0, 8)
        };
        root.Children.Add(scroll);
        return root;
    }

    static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
        FontSize = 11,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 9, 0, 3)
    };

    static void AddBox(Panel panel, TextBox box, string tooltip)
    {
        box.MinHeight = 32;
        box.Padding = new Thickness(8, 4, 8, 4);
        box.Margin = new Thickness(0, 0, 0, 1);
        box.ToolTip = tooltip;
        panel.Children.Add(box);
    }

    static TextBlock Help(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x7C, 0x8C)),
        FontSize = 10,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2)
    };

    void Fill()
    {
        _enabled.IsChecked = _preferences.LeagueCloudEnabled;
        _upload.Text = _preferences.LeagueCloudUploadUrl;
        _feed.Text = _preferences.LeagueCloudFeedUrl;
        _league.Text = _preferences.LeagueCloudLeagueId;
        _poll.Text = _preferences.LeagueCloudPollSeconds.ToString(CultureInfo.InvariantCulture);
        _key.Text = _preferences.LeagueCloudApiKey;
    }

    bool Save()
    {
        _preferences.LeagueCloudEnabled = _enabled.IsChecked == true;
        _preferences.LeagueCloudUploadUrl = (_upload.Text ?? "").Trim();
        _preferences.LeagueCloudFeedUrl = (_feed.Text ?? "").Trim();
        _preferences.LeagueCloudLeagueId = string.IsNullOrWhiteSpace(_league.Text) ? "default" : _league.Text.Trim();
        _preferences.LeagueCloudApiKey = (_key.Text ?? "").Trim();
        if (int.TryParse(_poll.Text, out var seconds)) _preferences.LeagueCloudPollSeconds = Math.Clamp(seconds, 5, 300);
        try { PreferencesStore.Save(_preferences); }
        catch (Exception ex) { _status.Text = $"Could not save preferences: {ex.Message}"; return false; }
        _service.ApplySettings();
        if (_preferences.LeagueCloudEnabled) _service.Start();
        return true;
    }

    void OnStatus(string message)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnStatus(message));
            return;
        }
        _status.Text = message;
    }

    void OnGamesChanged(IReadOnlyList<LeagueGame> games)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnGamesChanged(games));
            return;
        }
        RefreshGames(games);
    }

    void RefreshGames(IReadOnlyList<LeagueGame>? games = null)
    {
        var selectedId = (_games.SelectedItem as LeagueGame)?.GameId;
        _shownGames = (games ?? _service.Games).Select(game => game.Copy()).ToList();
        _games.ItemsSource = _shownGames;
        if (selectedId is not null)
            _games.SelectedItem = _shownGames.FirstOrDefault(game =>
                string.Equals(game.GameId, selectedId, StringComparison.OrdinalIgnoreCase));
    }

    void ImportSelected()
    {
        if (_games.SelectedItem is not LeagueGame game)
        {
            _status.Text = "Select a received game first, then tick the fields to import.";
            return;
        }
        var fields = new List<LeagueImportField>();
        if (_importHome.IsChecked == true) fields.Add(LeagueImportField.HomeScore);
        if (_importAway.IsChecked == true) fields.Add(LeagueImportField.AwayScore);
        if (_importClock.IsChecked == true) fields.Add(LeagueImportField.GameClock);
        if (_importPeriod.IsChecked == true) fields.Add(LeagueImportField.Period);
        if (fields.Count == 0)
        {
            _status.Text = "Tick at least one field to import.";
            return;
        }
        if (fields.Contains(LeagueImportField.GameClock) && !LeagueCloudImport.TryParseClock(game.Clock, out _))
        {
            _status.Text = $"“{game.Label}” has no usable game clock (“{game.Clock}”).";
            return;
        }
        var applied = LeagueCloudImport.Apply(_engine, game, fields);
        _status.Text = applied.Count == 0
            ? $"Nothing changed for “{game.Label}”."
            : $"Imported {string.Join(", ", applied)} from “{game.Label}”. Other scoreboard values were left alone.";
    }

    void AddTicker()
    {
        var displayIndex = _catalog.Displays.FindIndex(display =>
            string.Equals(display.Id, _program.DisplayId, StringComparison.OrdinalIgnoreCase));
        if (displayIndex < 0) displayIndex = _catalog.ActiveDisplayIndex;
        _catalog.SelectDisplay(displayIndex);
        var source = _catalog.Layout;
        var width = Math.Max(640, source.Width);
        var height = Math.Max(360, source.Height);
        _catalog.AddLayout("League Ticker");
        _catalog.Update(layout =>
        {
            layout.Width = width;
            layout.Height = height;
            layout.Background = "#00000000";
            layout.Elements.Clear();
            layout.Elements.Add(new ScorebugElement
            {
                Name = "League Ticker",
                Kind = ScorebugElementKind.Text,
                Content = "{{LEAGUE_GAMES}}",
                Ticker = true,
                X = 70,
                Y = height - 92,
                Width = width - 140,
                Height = 58,
                FontSize = 22,
                Foreground = "#FFFFFFFF",
                Background = "#E6080B10",
                BorderColor = "#00000000",
                BorderThickness = 0,
                TextAlign = ScorebugTextAlign.Left
            });
        });
        _status.Text = "League ticker layout added. It follows the live cloud feed; style it here, then Update Output.";
    }
}
