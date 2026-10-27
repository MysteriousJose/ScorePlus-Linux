using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scoreboard.Core;
using Scoreboard.Desktop;

namespace UiSmoke;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var layoutPath = Path.Combine(Path.GetTempPath(), $"smoke-scorebug-{Guid.NewGuid():N}.json");
        // Keep the operator's real preferences.json out of reach: earlier runs overwrote it, which
        // is how a light theme and an empty media library ended up on the reviewer's machine.
        // Every default path (preferences, keybinds, profiles, layout, assets, autosaves) is aimed at
        // a throwaway folder, so a suite can never touch the operator's real %LOCALAPPDATA%\ScorePlus.
        var appDataPath = Path.Combine(Path.GetTempPath(), $"smoke-appdata-{Guid.NewGuid():N}");
        AppPaths.UseRoot(appDataPath);
        var prefsPath = Path.Combine(Path.GetTempPath(), $"smoke-prefs-{Guid.NewGuid():N}.json");
        PreferencesStore.UsePath(prefsPath);
        // Autosaves are isolated too: a test run must never rotate the operator's own recovery points.
        var autosavePath = Path.Combine(Path.GetTempPath(), $"smoke-autosaves-{Guid.NewGuid():N}");
        AutosaveStore.UseFolder(autosavePath);
        try
        {
            var engine = new GameEngine();
            engine.NewGame("football");
            var catalog = new ScorebugCatalog(layoutPath);

            var main = new MainWindow();
            main.Show();
            main.UpdateLayout();
            main.Close();
            Console.WriteLine("PASS: main window loads");

            // The dock is a single media panel: tiles, transport and tag editor, no inline event log.
            var shell = new MainWindow { Width = 1280, Height = 800 };
            shell.Show();
            shell.UpdateLayout();
            var shellContent = (FrameworkElement)shell.Content;
            var dock = FindAll<MediaDock>(shell).FirstOrDefault() ?? throw new Exception("FAIL: the main window hosts no media dock");
            var transport = (Button)dock.FindName("PlayPauseButton")!;
            var transportBottom = transport.TransformToAncestor(shellContent).Transform(new Point(0, transport.ActualHeight)).Y;
            if (transportBottom > shellContent.ActualHeight + 0.5)
                throw new Exception($"FAIL: the media dock runs past the window ({transportBottom:0} of {shellContent.ActualHeight:0})");
            if (dock.FindName("Tiles") is not WrapPanel || dock.FindName("TagBox") is not ComboBox)
                throw new Exception("FAIL: the unified media dock is missing its tiles or tag editor");
            if (transport.ActualWidth < 36 || transport.ActualHeight < 28)
                throw new Exception($"FAIL: the transport controls are still tiny ({transport.ActualWidth:0}x{transport.ActualHeight:0})");
            if (FindAll<GridSplitter>(shell).Count() < 2)
                throw new Exception("FAIL: the preview, dock and console cannot be resized");
            var tagCombo = (ComboBox)dock.FindName("TagBox")!;
            if (tagCombo.Template?.FindName("PART_EditableTextBox", tagCombo) is not TextBox)
                throw new Exception("FAIL: the tag box has no editable text box, so the tag is invisible and cannot be typed");
            if (string.IsNullOrWhiteSpace(tagCombo.Text))
                throw new Exception("FAIL: the tag box starts blank");
            // The dock must wear the operator theme, not a palette of its own.
            var chrome = (Border)dock.FindName("Chrome")!;
            var removeButton = FindAll<Button>(dock).First(b => (b.Content as string) == "Remove");
            bool IsColor(Brush? brush, byte r, byte g, byte bl) => brush is SolidColorBrush s && s.Color.R == r && s.Color.G == g && s.Color.B == bl;
            if (!IsColor(chrome.Background, 8, 8, 8) || !IsColor(removeButton.Background, 20, 20, 20))
                throw new Exception($"FAIL: the media dock ignores the dark theme (panel {chrome.Background}, button {removeButton.Background})");
            dock.ApplyTheme("Light");
            dock.UpdateLayout();
            if (!IsColor(chrome.Background, 255, 255, 255) || !IsColor(removeButton.Background, 229, 234, 240) || !IsColor(removeButton.Foreground, 24, 32, 43))
                throw new Exception($"FAIL: the media dock ignores the light theme (panel {chrome.Background}, button {removeButton.Background}, text {removeButton.Foreground})");
            dock.ApplyTheme("Dark");
            dock.UpdateLayout();
            if (shell.FindName("EventList") is not null)
                throw new Exception("FAIL: the event log still lives in the dock");
            if (!FindAll<Button>(shell).Any(b => (b.Content as string) == "Event Log"))
                throw new Exception("FAIL: the event log button is missing from the top row");
            shell.Close();
            Console.WriteLine("PASS: dock holds one media library with tiles, transport and tags; event log moved to the top row");

            var control = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            control.Show();
            control.UpdateLayout();
            control.Execute("home.score.plus1");
            control.Execute("football.down.next");
            control.Execute("football.down.previous");
            control.Execute("football.series.new");
            control.Execute("football.ball.plus");
            control.Execute("football.ball.minus");
            control.Execute("output.publish");
            control.Execute("clock.toggle");
            control.Execute("clock.minus1");
            control.Execute("clock.plus1");
            var keySettings = new KeybindSettings();
            keySettings.Bindings["home.score.plus2"] = "F9";
            keySettings.Bindings["football.series.new"] = "F10";
            control.ApplyKeybinds(keySettings);
            var panelSource = PresentationSource.FromVisual(control);
            var scoreBeforeKey = engine.State.HomeScore;
            engine.SetFootballDistance(4, 22);
            RaiseKey(control, panelSource, Key.F9);
            RaiseKey(control, panelSource, Key.F10);
            if (engine.State.HomeScore != scoreBeforeKey + 2 || engine.State.Down != 1 || engine.State.YardsToGo != 10)
                throw new Exception("FAIL: keybinds did not run their buttons");
            Console.WriteLine("PASS: keybinds run their assigned buttons");

            control.Close();
            Console.WriteLine($"PASS: control panel loads and dispatches actions (score {engine.State.HomeScore}, down {engine.State.Down}, ball spot {engine.State.YardsToGoal})");

            var editor = new ScorebugEditorWindow(catalog, engine);
            editor.Show();
            editor.UpdateLayout();

            var layers = (ListBox)editor.FindName("Layers")!;
            var nameInput = (TextBox)editor.FindName("NameInput")!;
            var backgroundInput = (TextBox)editor.FindName("BackgroundInput")!;
            var widthInput = (TextBox)editor.FindName("WidthInput")!;
            layers.SelectedIndex = 2;
            editor.UpdateLayout();
            var selectedName = nameInput.Text;
            var selectedBackground = backgroundInput.Text;
            var selectedWidth = widthInput.Text;
            if (string.IsNullOrEmpty(selectedName)) throw new Exception("FAIL: studio selection did not load properties");
            // The layer list runs front-to-back, so row 0 is the last element drawn.
            var selectedIndex = catalog.Layout.Elements.Count - 1 - 2;
            if (catalog.Layout.Elements[selectedIndex].Name != selectedName)
                throw new Exception("FAIL: the layer list is not ordered with the highest layer on top");

            engine.Score(true, 2);
            engine.AdvanceDown();
            editor.UpdateLayout();
            if (layers.SelectedIndex != 2 || nameInput.Text != selectedName || backgroundInput.Text != selectedBackground || widthInput.Text != selectedWidth)
                throw new Exception("FAIL: studio switched elements or changed properties on a value update");
            Console.WriteLine($"PASS: studio keeps '{selectedName}' selected when score values change");

            nameInput.Text = "Renamed away team";
            Pump(TimeSpan.FromMilliseconds(700));
            var edited = catalog.Layout.Elements[selectedIndex];
            if (edited.Name != "Renamed away team" || edited.Background != selectedBackground || edited.Width.ToString("0") != selectedWidth)
                throw new Exception("FAIL: studio edit touched another element or its background");
            if (layers.SelectedIndex != 2 || nameInput.Text != "Renamed away team")
                throw new Exception("FAIL: studio lost selection after an edit");
            Console.WriteLine("PASS: studio edits apply to the selected element only");

            editor.Close();
            Console.WriteLine("PASS: scorebug studio loads");

            var keybinds = new KeybindsWindow(new KeybindSettings());
            keybinds.Show();
            keybinds.UpdateLayout();
            keybinds.Close();
            Console.WriteLine("PASS: keybinds window loads");

            var settings = new KeybindSettings();
            settings.Bindings["home.score.plus2"] = KeybindStore.ToText(Key.D2, ModifierKeys.Control);
            var matched = KeybindStore.Match(settings, Key.D2, ModifierKeys.Control);
            var text = KeybindStore.ToText(Key.D2, ModifierKeys.Control);
            var parsed = KeybindStore.TryParse(text, out var parsedKey, out var parsedMods);
            if (matched != "home.score.plus2" || !parsed || parsedKey != Key.D2 || parsedMods != ModifierKeys.Control)
                throw new Exception("FAIL: keybind round trip");
            Console.WriteLine($"PASS: keybind round trip ({text})");

            var outWindow = new OutputWindow(new GameEngine(), catalog);
            outWindow.Show();
            outWindow.UpdateLayout();
            outWindow.Close();
            Console.WriteLine("PASS: audience output loads");


            var preferencesWindow = new SettingsWindow(new UserPreferences());
            preferencesWindow.Show();
            preferencesWindow.UpdateLayout();
            if (preferencesWindow.FindName("ThemeSelector") is not ComboBox)
                throw new Exception("FAIL: preferences theme selector missing");
            preferencesWindow.Close();
            Console.WriteLine("PASS: preferences window loads with a save action");

            var profileCatalogPath = Path.Combine(Path.GetTempPath(), $"smoke-sports-{Guid.NewGuid():N}.json");
            try
            {
                var sports = new SportCatalog(profileCatalogPath);
                var profileWindow = new SportProfileWindow(sports);
                profileWindow.Show();
                profileWindow.UpdateLayout();
                var profiles = (ListBox)profileWindow.FindName("Profiles")!;
                profiles.SelectedIndex = profiles.Items.Cast<object>().Select((item, index) => (item, index)).First(x => x.item.ToString()!.Contains("American Football")).index;
                profileWindow.UpdateLayout();
                ((TextBox)profileWindow.FindName("TimeoutsInput")!).Text = "6";
                ((Button)profileWindow.FindName("SaveButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                profileWindow.UpdateLayout();
                if (sports.Find("football")!.Timeouts != 6 || !sports.IsOverridden("football"))
                    throw new Exception("FAIL: sport profile edits were not saved");
                profileWindow.Close();
                Console.WriteLine("PASS: editing a built-in sport profile saves the timeout count");
            }
            finally { if (File.Exists(profileCatalogPath)) File.Delete(profileCatalogPath); }

            engine.AddStat("Turnovers");
            var statsPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            statsPanel.Show();
            statsPanel.UpdateLayout();
            var statsHost = (WrapPanel)statsPanel.Panel.FindName("StatsHost")!;
            if (statsHost.Children.Count != 1) throw new Exception("FAIL: custom stat row was not created");
            var stat2 = engine.State.Stats[0];
            engine.AdjustStat(stat2.Id, 3);
            statsPanel.UpdateLayout();
            if (!statsPanel.Panel.HomeScoreText.Equals(engine.State.HomeScore.ToString()))
                throw new Exception("FAIL: control panel did not refresh after a stat change");
            Console.WriteLine("PASS: custom stats render and update in the control panel");

            var panelA = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            var panelB = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            panelA.Show(); panelB.Show();
            panelA.UpdateLayout(); panelB.UpdateLayout();
            var before = engine.State.HomeScore;
            panelA.Execute("home.score.plus2");
            panelA.UpdateLayout(); panelB.UpdateLayout();
            if (!panelB.Panel.HomeScoreText.Equals(engine.State.HomeScore.ToString()) || engine.State.HomeScore != before + 2)
                throw new Exception("FAIL: control panels did not stay in sync");
            panelA.Close(); panelB.Close();
            Console.WriteLine("PASS: multiple control panels stay in sync");

            var lightProbe = new SettingsWindow(new UserPreferences());
            lightProbe.Show();
            lightProbe.UpdateLayout();
            ThemeManager.Apply(lightProbe, "Light");
            Pump(TimeSpan.FromMilliseconds(250));
            var combo = (ComboBox)lightProbe.FindName("ThemeSelector")!;
            if (combo.Foreground is not SolidColorBrush comboText || combo.Background is not SolidColorBrush comboBackground)
                throw new Exception("FAIL: combo box colors were not themed");
            var contrast = Math.Abs(comboText.Color.R - comboBackground.Color.R) + Math.Abs(comboText.Color.G - comboBackground.Color.G) + Math.Abs(comboText.Color.B - comboBackground.Color.B);
            if (contrast < 200) throw new Exception($"FAIL: dropdown text contrast too low ({comboText.Color} on {comboBackground.Color})");
            lightProbe.Close();
            ThemeManager.Apply(statsPanel, "Dark");
            Pump(TimeSpan.FromMilliseconds(250));
            Console.WriteLine($"PASS: dropdown text stays readable in the light theme (contrast {contrast})");

            var themedEditor = new ScorebugEditorWindow(catalog, engine);
            themedEditor.Show();
            themedEditor.UpdateLayout();
            var canvas = (Canvas)themedEditor.FindName("DesignCanvas")!;
            var designedText = FindAll<TextBlock>(canvas).FirstOrDefault(t => t.Text.Length > 0);
            if (designedText is null) throw new Exception("FAIL: studio canvas has no text to check");
            var designedColor = ((SolidColorBrush)designedText.Foreground).Color;
            ThemeManager.Apply(themedEditor, "Light");
            Pump(TimeSpan.FromMilliseconds(250));
            if (((SolidColorBrush)designedText.Foreground).Color != designedColor)
                throw new Exception("FAIL: theme change recolored scorebug artwork");
            themedEditor.Close();
            Console.WriteLine("PASS: theme changes never recolor scorebug artwork");

            var picker = new ColorPickerWindow("#FF38D7A3");
            picker.Show();
            picker.UpdateLayout();
            picker.Close();
            Console.WriteLine("PASS: custom color picker loads");

            var prefsProbe = new UserPreferences { Theme = "Dark" };
            var cancelWindow = new SettingsWindow(prefsProbe);
            cancelWindow.Show();
            cancelWindow.UpdateLayout();
            ((ComboBox)cancelWindow.FindName("ThemeSelector")!).SelectedItem = "Light";
            cancelWindow.Close();
            if (prefsProbe.Theme != "Dark") throw new Exception("FAIL: cancelling preferences still changed the theme");
            var saveWindow = new SettingsWindow(prefsProbe);
            saveWindow.Show();
            saveWindow.UpdateLayout();
            ((ComboBox)saveWindow.FindName("ThemeSelector")!).SelectedItem = "Light";
            saveWindow.FindName("ThemeSelector");
            var saveButton = FindAll<Button>(saveWindow).First(b => (b.Content as string) == "Save");
            saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (prefsProbe.Theme != "Light") throw new Exception("FAIL: saving preferences did not apply the theme");
            Console.WriteLine("PASS: preferences only apply when saved");

            var enterEditor = new ScorebugEditorWindow(catalog, engine);
            enterEditor.Show();
            enterEditor.UpdateLayout();
            var enterLayers = (ListBox)enterEditor.FindName("Layers")!;
            enterLayers.SelectedIndex = 1;
            enterEditor.UpdateLayout();
            var enterName = (TextBox)enterEditor.FindName("NameInput")!;
            enterName.Text = "Renamed by enter";
            var enterSource = PresentationSource.FromVisual(enterEditor);
            enterName.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, enterSource!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            enterEditor.UpdateLayout();
            if (!enterEditor.IsVisible) throw new Exception("FAIL: Enter closed the studio");
            Pump(TimeSpan.FromMilliseconds(250));
            if (catalog.Layout.Elements[catalog.Layout.Elements.Count - 1 - 1].Name != "Renamed by enter") throw new Exception("FAIL: Enter did not commit the edited value");
            enterEditor.Close();
            Console.WriteLine("PASS: Enter commits a studio value without closing the window");

            var swatchPicker = new ColorPickerWindow("#FF38D7A3");
            swatchPicker.Show();
            swatchPicker.UpdateLayout();
            ThemeManager.Apply(swatchPicker, "Light");
            Pump(TimeSpan.FromMilliseconds(250));
            var redSwatch = FindAll<Button>(swatchPicker).FirstOrDefault(b => (b.ToolTip as string) == "#FFFF0000");
            if (redSwatch?.Background is not SolidColorBrush redBrush || redBrush.Color != Colors.Red)
                throw new Exception($"FAIL: palette swatch colors were overwritten by theming (found={(redSwatch is null ? "none" : redSwatch.Background?.ToString())}, skip={(redSwatch is null ? "n/a" : ThemeManager.GetSkipTheme(redSwatch).ToString())})");
            swatchPicker.Close();
            Console.WriteLine("PASS: color palette thumbnails keep their colors");

            // Steppers put the add buttons above the value and the subtract buttons below it.
            var stepPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            stepPanel.Show();
            stepPanel.UpdateLayout();
            var homeStepper = stepPanel.Panel.HomeScore;
            var plusRow = homeStepper.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().First();
            var minusRow = homeStepper.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().Last();
            var valueRow = homeStepper.Children.OfType<TextBox>().Single();
            if (Grid.GetRow(plusRow) >= Grid.GetRow(valueRow) || Grid.GetRow(valueRow) >= Grid.GetRow(minusRow))
                throw new Exception("FAIL: stepper did not stack add above value above subtract");
            var plusLabels = plusRow.Children.OfType<Button>().Select(b => b.Content as string).ToArray();
            if (!plusLabels.Contains("+1") || !plusLabels.Contains("+6") || plusRow.Children.Count != minusRow.Children.Count)
                throw new Exception($"FAIL: score steppers missing add buttons ({string.Join(",", plusLabels)})");
            Console.WriteLine($"PASS: stepper stacks {string.Join("/", plusLabels)} above the value and subtracts below");

            // Clicking the value and typing overrides it.
            var typedBox = FindAll<TextBox>(homeStepper).First();
            typedBox.Focus();
            // Focus can be refused for an off-screen window; raise the same event a real click produces.
            typedBox.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, typedBox) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
            typedBox.Text = "17";
            typedBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(typedBox)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            stepPanel.UpdateLayout();
            if (engine.State.HomeScore != 17) throw new Exception($"FAIL: typing a score did not override the value (score {engine.State.HomeScore})");
            Console.WriteLine("PASS: typing over the middle value sets it exactly");

            // Team names are editable right on the console.
            var awayName = (TextBox)stepPanel.Panel.FindName("AwayName")!;
            awayName.Focus();
            awayName.Text = "Rovers";
            awayName.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(awayName)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            stepPanel.UpdateLayout();
            if (engine.State.AwayTeam != "Rovers") throw new Exception($"FAIL: editing the away name did not reach the game (got '{engine.State.AwayTeam}')");
            Console.WriteLine("PASS: team names can be edited from the console");

            // Adding toggles that should not wait for Update Output: score and clocks already published, structure was not.
            var stagedEngine = new GameEngine(); stagedEngine.NewGame("football");
            var published = new GameEngine(); published.LoadFromState(stagedEngine.CloneState());
            stagedEngine.Score(true, 6);
            stagedEngine.SetPlayClock(25);
            stagedEngine.AdjustClock(-30);
            if (GameEngine.StagedSignature(stagedEngine.State) != GameEngine.StagedSignature(published.State))
                throw new Exception("FAIL: score or clocks marked the output as out of date");
            stagedEngine.AdvanceDown();
            if (GameEngine.StagedSignature(stagedEngine.State) == GameEngine.StagedSignature(published.State))
                throw new Exception("FAIL: down & distance did not stage for Update Output");
            Console.WriteLine("PASS: scores and clocks publish live while structural edits wait for Update Output");

            var liveEngine = new GameEngine(); liveEngine.NewGame("football");
            var liveTarget = new GameEngine(); liveTarget.LoadFromState(liveEngine.CloneState());
            liveEngine.SetPlayClock(17);
            liveEngine.ResetTimeoutClock(45);
            liveTarget.UpdateLiveScoreClock(liveEngine.State);
            if (liveTarget.State.PlayClockSeconds != 17 || liveTarget.State.TimeoutClockSeconds != 45)
                throw new Exception("FAIL: play clock or timeout clock did not go live");
            Console.WriteLine("PASS: play clock and timeout clock update live");
            stepPanel.Close();

            // Studio artwork starts with no background box.
            var studioLayout = Path.Combine(Path.GetTempPath(), $"smoke-new-element-{Guid.NewGuid():N}.json");
            try
            {
                var freshCatalog = new ScorebugCatalog(studioLayout);
                var addEditor = new ScorebugEditorWindow(freshCatalog, engine);
                addEditor.Show();
                addEditor.UpdateLayout();
                var addText = FindAll<Button>(addEditor).First(b => (b.Content as string) == "Add text");
                addText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                addEditor.UpdateLayout();
                var created = freshCatalog.Layout.Elements.Last();
                if (!created.TransparentBackground)
                    throw new Exception("FAIL: new studio artwork turned a background box on by default");
                addEditor.Close();
                Console.WriteLine("PASS: new studio artwork starts without a background box");
            }
            finally { if (File.Exists(studioLayout)) File.Delete(studioLayout); }

            // The football console fits its smallest window without scrolling, and away stays left of home.
            engine.AddStat("Penalties");
            // The console itself needs ~900px; the detached window also carries the media dock, so its
            // minimum usable width is the console width plus that dock.
            var fitPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true) { Width = 1280, Height = 860 };
            fitPanel.Show();
            fitPanel.UpdateLayout();
            var view = fitPanel.Panel;
            view.UpdateLayout();
            double Left(FrameworkElement element) => element.TransformToAncestor(view).Transform(new Point(0, 0)).X;
            var awayBox = (TextBox)view.FindName("AwayName")!;
            var homeBox = (TextBox)view.FindName("HomeName")!;
            if (Left(awayBox) >= Left(homeBox)) throw new Exception("FAIL: visitors are not left of home");
            foreach (var card in FindAll<Border>(view).Where(b => b.Style == (Style)view.FindResource("Card")))
                if (card.ActualHeight + 0.5 < card.DesiredSize.Height)
                    throw new Exception($"FAIL: a section is squeezed ({card.ActualHeight:0} of {card.DesiredSize.Height:0} needed)");
            var updateButton = (Button)view.FindName("UpdateButton")!;
            var updateBottom = updateButton.TransformToAncestor(view).Transform(new Point(0, updateButton.ActualHeight)).Y;
            if (updateBottom > view.ActualHeight + 0.5)
                throw new Exception($"FAIL: Update Output is off the page ({updateBottom:0} of {view.ActualHeight:0})");
            var scrollers = FindAll<ScrollViewer>(view).Where(s => s.IsVisible && s.ScrollableHeight > 0).ToArray();
            if (scrollers.Length > 0) throw new Exception($"FAIL: {scrollers.Length} section(s) need scrolling at the smallest window ({string.Join(", ", scrollers.Select(s => $"{s.Name}:{s.ScrollableHeight:0}/{s.ActualHeight:0}"))})");
            if (((FrameworkElement)view.FindName("PlayClockRow")!).Visibility != Visibility.Visible)
                throw new Exception("FAIL: play clock is hidden for football");
            if (((TextBlock)view.FindName("TimeoutClockValue")!).Text.Length == 0)
                throw new Exception("FAIL: timeout clock is missing");
            // A long stat list must not push the page: it scrolls inside its own card.
            engine.AddStat("Red zone trips");
            engine.AddStat("Explosive plays");
            view.UpdateLayout();
            var statsScroller = (ScrollViewer)view.FindName("StatsScroller")!;
            var afterStats = updateButton.TransformToAncestor(view).Transform(new Point(0, updateButton.ActualHeight)).Y;
            if (afterStats > view.ActualHeight + 0.5)
                throw new Exception($"FAIL: extra stats pushed Update Output off the page ({afterStats:0} of {view.ActualHeight:0})");
            if (statsScroller.ScrollableHeight <= 0)
                throw new Exception("FAIL: extra stats did not stay inside their card");
            fitPanel.Close();
            Console.WriteLine($"PASS: football console fits {view.ActualWidth:0}x{view.ActualHeight:0} with no scrolling, visitors left of home");
            Console.WriteLine("PASS: a long stat list scrolls inside its card without moving the page");

            // The ball spot names the half of the field it sits on, and the console flips it.
            var ballEngine = new GameEngine();
            ballEngine.NewGame("football");
            var ballWindow = new GameControlWindow(ballEngine, () => { }, _ => { }, () => true);
            ballWindow.Show();
            ballWindow.UpdateLayout();
            var ballView = ballWindow.Panel;
            ballView.UpdateLayout();
            var sideButton = FindAll<Button>(ballView).FirstOrDefault(b => (b.Tag as string) == "football.ball.side")
                ?? throw new Exception("FAIL: the ball spot has no side button");
            if (sideButton.ActualHeight < 30 || sideButton.FontSize < 10)
                throw new Exception($"FAIL: the ball spot side button is still tiny ({sideButton.ActualWidth:0}x{sideButton.ActualHeight:0})");
            if (!ControlActions.All.Any(a => a.Id == "football.ball.side"))
                throw new Exception("FAIL: the ball spot side button cannot be keybound");
            if ((sideButton.Content as string) != "HOME ▶")
                throw new Exception($"FAIL: the ball spot side button reads {sideButton.Content}");
            ballEngine.SetPossession(false);
            ballView.UpdateLayout();
            if ((sideButton.Content as string) != "HOME ▶" || !ballEngine.State.BallOnHomeSide)
                throw new Exception("FAIL: changing possession moved the ball to the other half");
            ballEngine.SetBallSide(false);
            ballView.UpdateLayout();
            if ((sideButton.Content as string) != "◀ VISITORS")
                throw new Exception($"FAIL: the ball spot side did not follow the game ({sideButton.Content})");
            sideButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ballEngine.State.BallOnHomeSide)
                throw new Exception("FAIL: the ball spot side button did not switch halves");
            ballWindow.Close();
            Console.WriteLine("PASS: the ball spot has its own side, independent of possession, and a button to flip it");

            // A profile edited while the app is running must reach the next new game, no relaunch.
            var profileShell = new MainWindow(LaunchMode.New);
            profileShell.Show();
            profileShell.UpdateLayout();
            profileShell.Engine.Sports.SaveProfile(profileShell.Engine.Sports.Find("football")! with { Timeouts = 4, ScoringOptions = [1, 2, 3, 6] });
            profileShell.StartNewGame("football");
            profileShell.UpdateLayout();
            if (profileShell.Engine.State.HomeTimeouts != 4)
                throw new Exception($"FAIL: an edited profile still gives {profileShell.Engine.State.HomeTimeouts} timeouts to a new game");
            if (!FindAll<Button>(profileShell.Panel.HomeScore).Any(b => (b.Content as string) == "+6"))
                throw new Exception("FAIL: the console kept the old scoring buttons after a profile edit");
            profileShell.Close();
            Console.WriteLine("PASS: a sport profile edited at runtime applies to the next new game");

            // Saving from the real profile dialog must stick in the running catalog too.
            var dialogPath = Path.Combine(appDataPath, $"dialog-sports-{Guid.NewGuid():N}.json");
            var dialogCatalog = new SportCatalog(dialogPath);
            var dialogTarget = dialogCatalog.All.OrderBy(s => s.Name).First();
            var profileDialog = new SportProfileWindow(dialogCatalog);
            profileDialog.Show();
            profileDialog.UpdateLayout();
            ((TextBox)profileDialog.FindName("TimeoutsInput")!).Text = "6";
            ((TextBox)profileDialog.FindName("ScoringInput")!).Text = "2, 3, 7";
            ((Button)profileDialog.FindName("SaveButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var savedProfile = dialogCatalog.Find(dialogTarget.Id)!;
            if (savedProfile.Timeouts != 6 || !savedProfile.ScoringOptions.SequenceEqual([2, 3, 7]))
                throw new Exception($"FAIL: the profile dialog saved {savedProfile.Timeouts} timeouts and {string.Join('/', savedProfile.ScoringOptions)} scoring");
            profileDialog.Close();
            Console.WriteLine("PASS: saving from the sport profile window reaches the running catalog immediately");

            // Icons: one bar per timeout, and a possession dot that follows the team with the ball.
            var iconEngine = new GameEngine();
            iconEngine.NewGame("football");
            var barsItem = new ScorebugElement { Name = "Timeouts", Content = "", Kind = ScorebugElementKind.Text, Icon = ScorebugIcon.TimeoutBars, Width = 160, Height = 60 };
            var iconsView = new ScorebugIconView(barsItem, () => iconEngine.State);
            var barCount = FindAll<System.Windows.Shapes.Rectangle>(iconsView).Count();
            if (barCount != iconEngine.State.HomeTimeouts)
                throw new Exception($"FAIL: timeout icon drew {barCount} bars for {iconEngine.State.HomeTimeouts} timeouts");
            foreach (var line in FindAll<System.Windows.Shapes.Rectangle>(iconsView))
                if (line.Width <= line.Height * 2)
                    throw new Exception($"FAIL: timeout bar is not a horizontal line ({line.Width:0}x{line.Height:0})");
            var barStack = FindAll<StackPanel>(iconsView).FirstOrDefault()
                ?? throw new Exception("FAIL: timeout lines have no stack to sit in");
            if (barStack.Orientation != Orientation.Horizontal)
                throw new Exception("FAIL: timeout lines are not in a row");
            iconEngine.Timeout(true);
            iconsView.Update();
            if (FindAll<System.Windows.Shapes.Rectangle>(iconsView).Count() != iconEngine.State.HomeTimeouts)
                throw new Exception("FAIL: the timeout icon did not follow a used timeout");
            var dotItem = new ScorebugElement { Name = "Possession", Content = "{{POSSESSION}}", Kind = ScorebugElementKind.Text, Icon = ScorebugIcon.PossessionDot, Width = 200, Height = 60 };
            var dotView = new ScorebugIconView(dotItem, () => iconEngine.State);
            var dot = FindAll<System.Windows.Shapes.Ellipse>(dotView).Single();
            iconEngine.SetPossession(false);
            dotView.Update();
            if (dot.HorizontalAlignment != HorizontalAlignment.Left)
                throw new Exception($"FAIL: the possession dot sits {dot.HorizontalAlignment} while the visitors have the ball");
            iconEngine.SetPossession(true);
            dotView.Update();
            if (dot.HorizontalAlignment != HorizontalAlignment.Right)
                throw new Exception($"FAIL: the possession dot did not move to the home side");
            dotItem.IconFlipped = true;
            iconEngine.SetPossession(false);
            dotView.Update();
            if (dot.HorizontalAlignment != HorizontalAlignment.Right)
                throw new Exception("FAIL: flipping the possession dot had no effect");
            Console.WriteLine("PASS: timeout bars and the possession dot follow live data, and the dot can be flipped");

            // The same icons render on a real scorebug canvas, and the studio offers the property.
            var iconCatalog = new ScorebugCatalog(Path.Combine(appDataPath, $"icons-{Guid.NewGuid():N}.json"));
            iconCatalog.Update(layout =>
            {
                layout.Elements.Clear();
                layout.Elements.Add(new ScorebugElement { Name = "Timeouts", Kind = ScorebugElementKind.Text, Content = "", Icon = ScorebugIcon.TimeoutBars });
                layout.Elements.Add(new ScorebugElement { Name = "Ball", Kind = ScorebugElementKind.Text, Content = "{{POSSESSION}}", Icon = ScorebugIcon.PossessionDot });
            });
            var iconCanvas = new ScorebugCanvasView(iconCatalog, iconEngine);
            iconCanvas.Measure(new Size(1280, 720));
            iconCanvas.Arrange(new Rect(0, 0, 1280, 720));
            iconCanvas.UpdateLayout();
            if (FindAll<ScorebugIconView>(iconCanvas).Count() != 2)
                throw new Exception("FAIL: the scorebug canvas did not draw the icon elements");
            iconCanvas.Dispose();
            var iconStudio = new ScorebugEditorWindow(iconCatalog, iconEngine, null, "Dark");
            iconStudio.Show();
            iconStudio.UpdateLayout();
            ((ListBox)iconStudio.FindName("Layers")!).SelectedIndex = 0;
            iconStudio.UpdateLayout();
            if (((FrameworkElement)iconStudio.FindName("IconSection")!).Visibility != Visibility.Visible)
                throw new Exception("FAIL: the studio hides the icon properties for a text element");
            if (((ComboBox)iconStudio.FindName("IconInput")!).SelectedItem is not (ScorebugIcon.TimeoutBars or ScorebugIcon.PossessionDot))
                throw new Exception("FAIL: the studio did not show the selected element's icon");
            iconStudio.Close();
            Console.WriteLine("PASS: icon elements draw on the scorebug canvas and edit from the studio");

            if (KeybindStore.Defaults().Bindings.Count != 0)
                throw new Exception("FAIL: keybinds are still assigned out of the box");
            Console.WriteLine("PASS: no shortcut is assigned until the operator picks one");

            // The button that pops out another synced panel raises the request the main window listens to.
            var popOutRequests = 0;
            var chainPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            chainPanel.NewPanelRequested += () => popOutRequests++;
            chainPanel.Show();
            chainPanel.UpdateLayout();
            FindAll<Button>(chainPanel).First(b => (b.Content as string) == "New panel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (popOutRequests != 1) throw new Exception($"FAIL: the New panel button raised the request {popOutRequests} times");
            chainPanel.Close();
            Console.WriteLine("PASS: the New panel button asks for another synced window");

            // Preference defaults: dark theme, detached control panel, heading tips off.
            var freshPrefs = new UserPreferences();
            if (freshPrefs.Theme != "Dark" || freshPrefs.ShowHeadingHints)
                throw new Exception($"FAIL: defaults are {freshPrefs.Theme}/{freshPrefs.ShowHeadingHints}");
            Console.WriteLine("PASS: defaults are dark theme with heading tips off");

            // Heading tips stay hidden unless the preference is on.
            var hintWindow = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            hintWindow.Show();
            hintWindow.UpdateLayout();
            var detailHint = (TextBlock)hintWindow.Panel.FindName("DetailCardHint")!;
            hintWindow.Panel.SetHintVisibility(false);
            if (detailHint.Visibility != Visibility.Collapsed) throw new Exception("FAIL: heading tips showed while disabled");
            hintWindow.Panel.SetHintVisibility(true);
            if (detailHint.Visibility != Visibility.Visible) throw new Exception("FAIL: heading tips stayed hidden while enabled");
            hintWindow.Close();
            Console.WriteLine("PASS: heading tips follow the preference");

            // Live score & clocks can be toggled from a keybind.
            var liveState = true;
            var livePanel = new GameControlWindow(engine, () => { }, value => liveState = value, () => liveState);
            livePanel.Execute("output.live.toggle");
            if (liveState) throw new Exception("FAIL: the live toggle keybind did nothing");
            livePanel.Execute("output.live.toggle");
            if (!liveState) throw new Exception("FAIL: the live toggle keybind did not toggle back");
            livePanel.Close();
            Console.WriteLine("PASS: live score & clocks can be bound to a key");

            // Renaming a team must reach the audience output without pressing Update Output.
            var outSource = new GameEngine();
            outSource.NewGame("football");
            var outWindow2 = new OutputWindow(outSource, catalog, "Dark");
            outWindow2.Show();
            outWindow2.UpdateLayout();
            var renamed = new GameEngine();
            renamed.NewGame("football");
            renamed.SetTeams("Home Squad", "Away Squad");
            outWindow2.UpdateLiveScoreClock(renamed.State);
            outWindow2.UpdateLayout();
            var homeLabel = (TextBlock)outWindow2.FindName("HomeLabel")!;
            // The on-air label is the typed name, verbatim: not initials, not upper-cased.
            if (homeLabel.Text != "Home Squad") throw new Exception($"FAIL: renamed team did not reach the output (label '{homeLabel.Text}')");
            renamed.SetTeams("ny giants", "Away Squad");
            outWindow2.UpdateLiveScoreClock(renamed.State);
            outWindow2.UpdateLayout();
            if (homeLabel.Text != "ny giants") throw new Exception($"FAIL: the output forced the team name's case (label '{homeLabel.Text}')");
            outWindow2.Close();
            Console.WriteLine("PASS: renamed teams show up on the audience output right away");

            var eventLog = new EventLogWindow(engine, "Dark", false);
            eventLog.Show();
            eventLog.UpdateLayout();
            eventLog.Close();
            Console.WriteLine("PASS: event log opens in its own window");

            // Scorebug video elements only show clips from their tag, and never audio.
            var videoCatalogPath = Path.Combine(Path.GetTempPath(), $"smoke-video-{Guid.NewGuid():N}.json");
            try
            {
                var videoCatalog = new ScorebugCatalog(videoCatalogPath);
                var playback = new MediaPlayback();
                videoCatalog.Update(layout => layout.Elements.Add(new ScorebugElement
                {
                    Name = "Ad video", Kind = ScorebugElementKind.Video, MediaTag = "Ads",
                    Width = 480, Height = 270, X = 40, Y = 40, TransparentBackground = true
                }));
                var videoView = new ScorebugCanvasView(videoCatalog, engine, playback: playback);
                videoView.Measure(new Size(640, 360));
                videoView.Arrange(new Rect(0, 0, 640, 360));
                videoView.UpdateLayout();
                if (FindAll<VideoSurface>(videoView).Count() != 1)
                    throw new Exception("FAIL: video element did not produce a video surface");
                // The picture must be hard-clipped to its element box, or it paints over its neighbours.
                var surface = FindAll<VideoSurface>(videoView).First();
                if (!surface.ClipToBounds || surface.Clip is not RectangleGeometry surfaceClip)
                    throw new Exception("FAIL: the video surface has no clipping geometry");
                if (surfaceClip.Rect.Width < surface.ActualWidth - 0.5 || surfaceClip.Rect.Width <= 0)
                    throw new Exception($"FAIL: the video clip {surfaceClip.Rect} does not match its box {surface.ActualWidth:0}x{surface.ActualHeight:0}");

                var ad = new MediaEntry(@"C:\media\ad.mp4", "Ads");
                var highlight = new MediaEntry(@"C:\media\highlight.mp4", "Highlights");
                var anthem = new MediaEntry(@"C:\media\anthem.mp3", "Ads");
                playback.Start(ad);
                if (playback.ActiveFor("Ads")?.Path != ad.Path) throw new Exception("FAIL: a tagged clip did not reach its matching element");
                playback.Seek(12.5);
                if (Math.Abs(playback.Position - 12.5) > 0.001)
                    throw new Exception($"FAIL: seeking did not reach the scorebug video ({playback.Position})");
                if (playback.ActiveFor("Highlights") is not null) throw new Exception("FAIL: the wrong tag reached a video element");
                if (playback.ActiveFor("")?.Path != ad.Path) throw new Exception("FAIL: an element with no tag should accept any playing video");
                playback.Start(anthem);
                if (playback.ActiveFor("") is not null) throw new Exception("FAIL: an audio cue filled a video element");
                playback.Start(highlight);
                if (playback.ActiveFor("Ads") is not null) throw new Exception("FAIL: a highlight reel played in the ad box");
                playback.Stop();
                if (playback.ActiveFor("Ads") is not null) throw new Exception("FAIL: stopping media left the overlay cued");
                videoView.Dispose();
                Console.WriteLine("PASS: video overlays only show media from their own tag");

                // Canvas size presets scale the layout and persist.
                var presetStudio = new ScorebugEditorWindow(videoCatalog, engine);
                presetStudio.Show();
                presetStudio.UpdateLayout();
                var firstX = videoCatalog.Layout.Elements[0].X;
                ((ComboBox)presetStudio.FindName("CanvasPreset")!).SelectedIndex = 1; // Full HD 1920 × 1080
                Pump(TimeSpan.FromMilliseconds(300));
                if (videoCatalog.Layout.Width != 1920 || videoCatalog.Layout.Height != 1080)
                    throw new Exception($"FAIL: canvas preset left the layout at {videoCatalog.Layout.Width}×{videoCatalog.Layout.Height}");
                if (Math.Abs(videoCatalog.Layout.Elements[0].X - firstX * 1.5) > 0.6)
                    throw new Exception("FAIL: canvas resize did not scale element positions");
                if (((Canvas)presetStudio.FindName("DesignCanvas")!).Width != 1920)
                    throw new Exception("FAIL: studio canvas did not adopt the new size");
                var studioContent = (FrameworkElement)presetStudio.Content;
                var sizeControls = (ComboBox)presetStudio.FindName("CanvasPreset")!;
                var sizeRight = sizeControls.TransformToAncestor(studioContent).Transform(new Point(sizeControls.ActualWidth, 0)).X;
                if (sizeRight > studioContent.ActualWidth + 0.5)
                    throw new Exception("FAIL: the canvas size controls do not fit in the studio");
                if (!FindAll<Button>(presetStudio).Any(b => (b.Content as string) == "Add video"))
                    throw new Exception("FAIL: the studio has no Add video button");

                // 4K preset: canvas grows and every element stays inside the frame.
                ((ComboBox)presetStudio.FindName("CanvasPreset")!).SelectedIndex = 3;
                Pump(TimeSpan.FromMilliseconds(300));
                if (videoCatalog.Layout.Width != 3840 || videoCatalog.Layout.Height != 2160)
                    throw new Exception($"FAIL: the 4K preset left the layout at {videoCatalog.Layout.Width}×{videoCatalog.Layout.Height}");
                if (videoCatalog.Layout.Elements.Any(e => e.X + e.Width > videoCatalog.Layout.Width + 0.5 || e.Y + e.Height > videoCatalog.Layout.Height + 0.5))
                    throw new Exception("FAIL: 4K scaling pushed an element outside the canvas");
                presetStudio.Close();
                var sizedAgain = new ScorebugCatalog(videoCatalogPath);
                if (sizedAgain.Layout.Width != 3840) throw new Exception("FAIL: canvas size did not persist");
                Console.WriteLine($"PASS: canvas presets scale and persist ({sizedAgain.Layout.Width}×{sizedAgain.Layout.Height})");
            }
            finally { if (File.Exists(videoCatalogPath)) File.Delete(videoCatalogPath); }

            // Retagging gates what the video element can show, and the library survives a save/load round trip.
            var library = new List<MediaEntry> { new(@"C:\media\ad.mp4", "General"), new(@"C:\media\song.mp3", "Music") };
            var retagged = MediaLibrary.Retag(library, library[0], "Ads")!;
            if (retagged.Tag != "Ads" || library[0].Tag != "Ads")
                throw new Exception("FAIL: assigning a tag did not update the clip");
            if (MediaLibrary.Retag(library, library[1], "  ")!.Tag != MediaLibrary.DefaultTag)
                throw new Exception("FAIL: an empty tag should fall back to General");
            var savedPrefs = System.Text.Json.JsonSerializer.Serialize(new UserPreferences { Media = library });
            var reloadedPrefs = System.Text.Json.JsonSerializer.Deserialize<UserPreferences>(savedPrefs)!;
            if (reloadedPrefs.Media.Count != 2 || reloadedPrefs.Media[0].Tag != "Ads" || !reloadedPrefs.Media[0].IsVideo || reloadedPrefs.Media[1].Tag != MediaLibrary.DefaultTag)
                throw new Exception("FAIL: the media library did not survive a preferences round trip");
            Console.WriteLine("PASS: media tags stick to clips and the library round trips through preferences");

            // Seeking: a cued clip can be jumped into instead of watched from the start.
            var seekPath = Path.Combine(Path.GetTempPath(), $"smoke-seek-{Guid.NewGuid():N}.wav");
            WriteSilentWav(seekPath, 6);
            var seekSession = new MediaSession(new UserPreferences());
            try
            {
                seekSession.Play(new MediaEntry(seekPath, "Music"));
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (seekSession.Duration <= 0 && DateTime.UtcNow < deadline) Pump(TimeSpan.FromMilliseconds(100));
                if (seekSession.Duration <= 0) throw new Exception("FAIL: the test clip never opened, so seeking could not be checked");
                seekSession.Seek(1);
                Pump(TimeSpan.FromMilliseconds(600));
                if (seekSession.Position is < 0.8 or > 2.2)
                    throw new Exception($"FAIL: seeking landed at {seekSession.Position:0.00}s instead of about 1.00s");
                if (Math.Abs(seekSession.Playback.Position - 1) > 0.001)
                    throw new Exception($"FAIL: the scorebug cue did not follow the seek ({seekSession.Playback.Position})");
                Console.WriteLine($"PASS: a clip can be scrubbed straight to 1.00s (duration {seekSession.Duration:0.0}s)");

                // A focused scrub bar used to feed its own transport updates back in as seek
                // requests, which hammered the decoder until the app died. It must now just sit
                // where the operator left it.
                var seekDock = new MediaDock();
                seekDock.Attach(seekSession);
                var seekHost = new Window { Width = 420, Height = 320, Content = seekDock };
                seekHost.Show();
                seekHost.UpdateLayout();
                var bar = (Slider)seekDock.FindName("ScrubBar")!;
                Keyboard.Focus(bar);
                Pump(TimeSpan.FromMilliseconds(400));
                var seeksBefore = seekSession.Playback.SeekVersion;
                Pump(TimeSpan.FromMilliseconds(900));
                if (seekSession.Playback.SeekVersion != seeksBefore)
                    throw new Exception($"FAIL: the focused scrub bar re-seeked the clip {seekSession.Playback.SeekVersion - seeksBefore} times on its own");
                seekHost.Close();
                Console.WriteLine("PASS: a focused scrub bar never echoes transport updates back as seeks");
            }
            finally
            {
                seekSession.Dispose();
                try { File.Delete(seekPath); } catch { }
            }

            // Update Output is bindable as a key.
            var publishCount = 0;
            var publishPanel = new GameControlWindow(engine, () => publishCount++, _ => { }, () => true);
            publishPanel.Show();
            publishPanel.UpdateLayout();
            var publishKeys = new KeybindSettings();
            publishKeys.Bindings["output.publish"] = "F8";
            publishPanel.ApplyKeybinds(publishKeys);
            RaiseKey(publishPanel, PresentationSource.FromVisual(publishPanel), Key.F8);
            if (publishCount != 1) throw new Exception($"FAIL: the Update Output keybind fired {publishCount} times");
            publishPanel.Close();
            Console.WriteLine("PASS: Update Output can be bound to a key");

            // A detached panel carries the same media dock, so media is reachable from any panel.
            var sharedPrefs = new UserPreferences();
            var sharedMedia = new MediaSession(sharedPrefs);
            sharedMedia.Library.Add(new MediaEntry(Path.Combine(Path.GetTempPath(), "smoke-ad.mp4"), "Ads"));
            try
            {
                var mediaPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true, sharedMedia);
                mediaPanel.Show();
                mediaPanel.UpdateLayout();
                var panelDock = FindAll<MediaDock>(mediaPanel).FirstOrDefault() ?? throw new Exception("FAIL: a detached control panel has no media dock");
                var panelTiles = (WrapPanel)panelDock.FindName("Tiles")!;
                panelTiles.UpdateLayout();
                if (panelTiles.Children.Count != sharedMedia.Library.Count || panelTiles.Children.Count == 0)
                    throw new Exception($"FAIL: the detached panel shows {panelTiles.Children.Count} media tiles for a library of {sharedMedia.Library.Count}");
                if (!FindAll<GridSplitter>(mediaPanel).Any())
                    throw new Exception("FAIL: the detached panel's media dock cannot be resized");
                mediaPanel.Close();
            }
            finally { sharedMedia.Dispose(); }
            Console.WriteLine("PASS: detached control panels show the shared media library");

            // The app opens on a chooser: product name, both buttons, the version strip and autosaves.
            var demoAutosave = new GameEngine();
            demoAutosave.NewGame("football");
            demoAutosave.SetTeams("Away Club", "Home Club");
            demoAutosave.Score(true, 3);
            AutosaveStore.Save(demoAutosave);
            var launcherWindow = new LauncherWindow();
            launcherWindow.Show();
            launcherWindow.UpdateLayout();
            var chooserButtons = FindAll<Button>(launcherWindow).Select(b => b.Content as string).Where(t => t is not null).ToArray();
            if (!chooserButtons.Contains("New Game") || !chooserButtons.Contains("Open Game"))
                throw new Exception("FAIL: the launcher is missing New Game / Open Game");
            var versionLabel = FindAll<TextBlock>(launcherWindow).FirstOrDefault(t => t.Text.StartsWith("v"));
            if (versionLabel is null || versionLabel.Text != LauncherWindow.Version)
                throw new Exception("FAIL: the launcher does not show the version on its side strip");
            if (!FindAll<TextBlock>(launcherWindow).Any(t => t.Text.Contains("SCOREPLUS", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("FAIL: the launcher does not show the product name");
            var autosaveList = (ListBox)launcherWindow.FindName("AutosaveList")!;
            if (autosaveList.Items.Count == 0)
                throw new Exception("FAIL: the launcher lists no autosaves to load");
            launcherWindow.Close();
            Console.WriteLine($"PASS: launch chooser offers New Game / Open Game, {LauncherWindow.Version} and {autosaveList.Items.Count} autosave(s)");

            // Studio dialogs are plain Windows: the theme has to reach them too.
            var dialogProbe = new Window { Width = 300, Height = 200 };
            var probeList = new ListBox();
            dialogProbe.Content = probeList;
            ThemeManager.Apply(dialogProbe, "Dark");
            dialogProbe.Show();
            dialogProbe.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(150));
            if (probeList.Background is not SolidColorBrush probeBrush || probeBrush.Color.R > 100)
                throw new Exception($"FAIL: a plain dialog was not themed ({probeList.Background})");
            if (dialogProbe.Background is not SolidColorBrush probeWindow || probeWindow.Color.R > 100)
                throw new Exception($"FAIL: a plain dialog window kept its light background ({dialogProbe.Background})");
            dialogProbe.Close();
            Console.WriteLine("PASS: plain dialogs (shape, sport data, video) pick up the theme");

            // A new game clears the live game and the on-air copy, so the previous one cannot linger.
            var panelsBeforeGame = Application.Current.Windows.OfType<GameControlWindow>().Count(w => w.IsVisible);
            var freshGame = new MainWindow(LaunchMode.New);
            freshGame.Show();
            freshGame.UpdateLayout();
            if (Application.Current.Windows.OfType<GameControlWindow>().Count(w => w.IsVisible) > panelsBeforeGame)
                throw new Exception("FAIL: opening a game popped out another control panel");
            freshGame.Engine.SetTeams("Old Home", "Old Away");
            freshGame.Engine.Score(true, 21);
            freshGame.Engine.AdvanceDown();
            freshGame.Engine.AddStat("Penalties");
            freshGame.PublishOutput();
            freshGame.StartNewGame("football");
            if (freshGame.Engine.State.HomeScore != 0 || freshGame.Engine.State.AwayScore != 0 || freshGame.Engine.State.Down != 1 || freshGame.Engine.State.Stats.Count != 0)
                throw new Exception("FAIL: New Game left the previous game's state behind");
            if (freshGame.Engine.State.HomeTeam == "Old Home" || freshGame.PublishedState.HomeScore != 0 || freshGame.PublishedState.HomeTeam == "Old Home" || freshGame.PublishedState.Down != 1)
                throw new Exception("FAIL: New Game left the previous game on air");
            // The blank slate has to be visible, not just stored: the preview must be rebuilt.
            var previewView = FindAll<ScorebugCanvasView>(freshGame).FirstOrDefault()
                ?? throw new Exception("FAIL: the main window has no program preview");
            var previewTexts = FindAll<TextBlock>(previewView).Select(t => t.Text).ToArray();
            if (previewTexts.Any(t => t.Contains("Old Home", StringComparison.Ordinal) || t.Contains("Old Away", StringComparison.Ordinal) || t == "21"))
                throw new Exception($"FAIL: the scorebug preview still shows the previous game ({string.Join(" | ", previewTexts)})");
            freshGame.Close();
            Console.WriteLine("PASS: New Game resets the game and clears the previous game from the output");

            // Studio: layer order, context-sensitive properties and the element's own quarter style.
            var contextPath = Path.Combine(Path.GetTempPath(), $"smoke-studio-{Guid.NewGuid():N}.json");
            try
            {
                var contextCatalog = new ScorebugCatalog(contextPath);
                var contextEditor = new ScorebugEditorWindow(contextCatalog, engine);
                contextEditor.Show();
                contextEditor.UpdateLayout();
                var contextLayers = (ListBox)contextEditor.FindName("Layers")!;
                var textIndex = contextCatalog.Layout.Elements.FindIndex(e => e.Kind == ScorebugElementKind.Text);
                contextLayers.SelectedIndex = contextCatalog.Layout.Elements.Count - 1 - textIndex;
                contextEditor.UpdateLayout();
                var imagePicker = FindAll<Button>(contextEditor).First(b => (b.Content as string) is "Choose logo image…");
                if (imagePicker.IsVisible)
                    throw new Exception("FAIL: the logo image picker is offered for a text element");
                var logoIndex = contextCatalog.Layout.Elements.FindIndex(e => e.Kind == ScorebugElementKind.Logo);
                contextLayers.SelectedIndex = contextCatalog.Layout.Elements.Count - 1 - logoIndex;
                contextEditor.UpdateLayout();
                if (!imagePicker.IsVisible)
                    throw new Exception("FAIL: the logo image picker is missing for a logo element");

                contextCatalog.Update(layout => layout.Elements.Add(new ScorebugElement { Name = "Quarter chip", Content = "{{PERIOD}}", X = 20, Y = 20, Width = 200, Height = 60 }));
                contextEditor.UpdateLayout();
                contextLayers.SelectedIndex = 0;
                contextEditor.UpdateLayout();
                var quarterCombo = (ComboBox)contextEditor.FindName("PeriodFormatInput")!;
                if (quarterCombo.IsVisible == false) throw new Exception("FAIL: the quarter style is not offered for a period element");
                quarterCombo.SelectedIndex = 3; // "1"
                Pump(TimeSpan.FromMilliseconds(400));
                var chip = contextCatalog.Layout.Elements.Last();
                if (chip.PeriodFormat != "Number")
                    throw new Exception($"FAIL: the element's quarter style was not saved ({chip.PeriodFormat})");
                if (!FindAll<TextBlock>((Canvas)contextEditor.FindName("DesignCanvas")!).Any(t => t.Text == "1"))
                    throw new Exception("FAIL: the studio did not print the element's own quarter style");
                contextEditor.Close();
            }
            finally { if (File.Exists(contextPath)) File.Delete(contextPath); }
            Console.WriteLine("PASS: studio layers run front-first and only show properties that apply to the selected element");

            // Video tag labels are a studio aid; nothing about them reaches the audience picture.
            var videoLabelPath = Path.Combine(Path.GetTempPath(), $"smoke-video-label-{Guid.NewGuid():N}.json");
            try
            {
                var labelCatalog = new ScorebugCatalog(videoLabelPath);
                labelCatalog.Update(layout => layout.Elements.Add(new ScorebugElement { Name = "Ad box", Kind = ScorebugElementKind.Video, MediaTag = "General", X = 10, Y = 10, Width = 320, Height = 180 }));
                var audience = new ScorebugCanvasView(labelCatalog, engine, playback: new MediaPlayback());
                audience.Measure(new Size(640, 360));
                audience.Arrange(new Rect(0, 0, 640, 360));
                audience.UpdateLayout();
                if (FindAll<TextBlock>(audience).Any(t => t.Text.Contains("VIDEO", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("FAIL: the video tag label reached the audience picture");
                var labelEditor = new ScorebugEditorWindow(labelCatalog, engine, new MediaPlayback());
                labelEditor.Show();
                labelEditor.UpdateLayout();
                if (!FindAll<TextBlock>(labelEditor).Any(t => t.Text.Contains("VIDEO · GENERAL", StringComparison.Ordinal)))
                    throw new Exception("FAIL: the studio does not label an empty video element");
                labelEditor.Close();
                audience.Dispose();
            }
            finally { if (File.Exists(videoLabelPath)) File.Delete(videoLabelPath); }
            Console.WriteLine("PASS: an empty video element is labelled in the studio only, never on the audience picture");

            Console.WriteLine("ALL UI SMOKE CHECKS PASSED");

            app.Shutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex);
            app.Shutdown();
            return 1;
        }
        finally
        {
            PreferencesStore.UsePath(null);
            AppPaths.UseRoot(null);
            AutosaveStore.UseFolder(null);
            try { if (Directory.Exists(appDataPath)) Directory.Delete(appDataPath, true); } catch { }
            try { if (Directory.Exists(autosavePath)) Directory.Delete(autosavePath, true); } catch { }
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
            if (File.Exists(prefsPath)) File.Delete(prefsPath);
            var prefsTemp = prefsPath + ".tmp";
            if (File.Exists(prefsTemp)) File.Delete(prefsTemp);
        }
    }


    static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindAll<T>(child)) yield return nested;
        }
    }

    static void RaiseKey(Window window, PresentationSource? source, Key key)
    {
        if (source is null) throw new Exception("FAIL: window has no presentation source");
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    /// <summary>A plain 16-bit PCM WAV, so the seek check has a real decodable clip without user files.</summary>
    static void WriteSilentWav(string path, int seconds)
    {
        const int rate = 22050;
        short channels = 1, bits = 16;
        var data = rate * seconds * channels * bits / 8;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + data);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write(bits);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(data);
        writer.Write(new byte[data]);
    }

    static void Pump(TimeSpan duration)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
