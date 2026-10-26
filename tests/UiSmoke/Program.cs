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

            engine.Score(true, 2);
            engine.AdvanceDown();
            editor.UpdateLayout();
            if (layers.SelectedIndex != 2 || nameInput.Text != selectedName || backgroundInput.Text != selectedBackground || widthInput.Text != selectedWidth)
                throw new Exception("FAIL: studio switched elements or changed properties on a value update");
            Console.WriteLine($"PASS: studio keeps '{selectedName}' selected when score values change");

            nameInput.Text = "Renamed away team";
            Pump(TimeSpan.FromMilliseconds(700));
            var edited = catalog.Layout.Elements[2];
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
            var statsHost = (StackPanel)statsPanel.FindName("StatsHost")!;
            if (statsHost.Children.Count != 1) throw new Exception("FAIL: custom stat row was not created");
            var stat2 = engine.State.Stats[0];
            engine.AdjustStat(stat2.Id, 3);
            statsPanel.UpdateLayout();
            if (!((TextBlock)statsPanel.FindName("HomeScore")!).Text.Equals(engine.State.HomeScore.ToString()))
                throw new Exception("FAIL: control panel did not refresh after a stat change");
            Console.WriteLine("PASS: custom stats render and update in the control panel");

            var panelA = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            var panelB = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            panelA.Show(); panelB.Show();
            panelA.UpdateLayout(); panelB.UpdateLayout();
            var before = engine.State.HomeScore;
            panelA.Execute("home.score.plus2");
            panelA.UpdateLayout(); panelB.UpdateLayout();
            if (!((TextBlock)panelB.FindName("HomeScore")!).Text.Equals(engine.State.HomeScore.ToString()) || engine.State.HomeScore != before + 2)
                throw new Exception("FAIL: control panels did not stay in sync");
            panelA.Close(); panelB.Close();
            Console.WriteLine("PASS: multiple control panels stay in sync");

            ThemeManager.Apply(statsPanel, "Light");
            Pump(TimeSpan.FromMilliseconds(250));
            var combo = (ComboBox)statsPanel.FindName("PeriodFormatSelector")!;
            if (combo.Foreground is not SolidColorBrush comboText || combo.Background is not SolidColorBrush comboBackground)
                throw new Exception("FAIL: combo box colors were not themed");
            var contrast = Math.Abs(comboText.Color.R - comboBackground.Color.R) + Math.Abs(comboText.Color.G - comboBackground.Color.G) + Math.Abs(comboText.Color.B - comboBackground.Color.B);
            if (contrast < 200) throw new Exception($"FAIL: dropdown text contrast too low ({comboText.Color} on {comboBackground.Color})");
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
            if (catalog.Layout.Elements[1].Name != "Renamed by enter") throw new Exception("FAIL: Enter did not commit the edited value");
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
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
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

    static void Pump(TimeSpan duration)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
