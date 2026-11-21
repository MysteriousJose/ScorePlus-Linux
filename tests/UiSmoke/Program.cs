using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Scoreboard.Core;
using Scoreboard.Desktop;
using Brushes = Scoreboard.Desktop.Brushes;
using Dispatcher = Avalonia.Threading.Dispatcher;
using FontWeights = Scoreboard.Desktop.FontWeights;
using Path = System.IO.Path;
using Visibility = Scoreboard.Desktop.Visibility;
using Window = Scoreboard.Desktop.Window;

namespace UiSmoke;

/// <summary>
/// One UI suite for every operating system. It drives the real Avalonia windows and controls on
/// Avalonia's headless platform, so Linux, Windows and macOS run the same assertions instead of
/// carrying separate WPF and Avalonia harnesses. Checks assert structure and behaviour, not pixel
/// sizes: fonts and window decorations legitimately differ between operating systems.
/// </summary>
internal static class Program
{
    [STAThread]
    static int Main()
    {
        // Every default path is aimed at a throwaway folder, so a run can never touch the
        // operator's real preferences, rosters, layouts or recovery points.
        var data = Path.Combine(Path.GetTempPath(), $"smoke-{Guid.NewGuid():N}");
        AppPaths.UseRoot(data);
        PreferencesStore.UsePath(Path.Combine(data, "preferences.json"));
        AutosaveStore.UseFolder(Path.Combine(data, "autosaves"));
        RosterStore.UsePath(Path.Combine(data, "rosters.json"));
        PreferencesStore.Save(new UserPreferences { CheckForUpdates = false, Theme = "Dark" });

        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .SetupWithoutStarting();

        var temps = new List<string>();
        try
        {
            if (!MainWindow.PromptOnExit)
                throw new Exception("FAIL: the unsaved-game prompt is not on by default");
            // These checks open and close windows constantly; a modal prompt would stall the run.
            // The prompt itself is covered by the dialog checks below.
            MainWindow.PromptOnExit = false;
            // The sport-switch warning is an operator guard these checks change sports past.
            MainWindow.WarnOnSportSwitch = false;

            ExitPromptChecks();
            ScreenFitChecks();
            ResponsiveLayoutChecks(temps);
            PaintTextureChecks(temps);
            ShellChecks();
            ControlPanelChecks();
            TickerChecks();
            LeagueCloudChecks();
            MacroChecks(temps);
            StudioChecks(temps);
            StudioFeatureChecks(temps);
            WindowCatalogChecks(temps);
            MediaBackendChecks(temps);
            PortedFeatureChecks(temps);
            DialogChecks();
            ThemeChecks();
            IconChecks(temps);
            VideoRoutingChecks(temps);
            MediaChecks(temps);
            GameWindowChecks(temps);
            LayoutChecks(temps);
            DisplayChecks(temps);
            UpdateChannelChecks();

            Console.WriteLine();
            Console.WriteLine("ALL UI CHECKS PASSED");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
        finally
        {
            MainWindow.PromptOnExit = false;
            PreferencesStore.UsePath(null);
            RosterStore.UsePath(null);
            AutosaveStore.UseFolder(null);
            foreach (var path in temps)
            {
                try { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); } catch { }
            }
            try { Directory.Delete(data, true); } catch { }
        }
    }

    // ---------------------------------------------------------------- checks

    static void ExitPromptChecks()
    {
        var prompt = new UnsavedChangesWindow("Dark", "A game is still in progress.");
        prompt.Show();
        prompt.UpdateLayout();
        var actions = All<Button>(prompt).Select(button => button.Content as string).ToArray();
        Check(actions.Count(text => text is "Keep editing" or "Exit without saving" or "Save & exit") == 3,
            "exiting a changed game offers keep, discard and save");
        Check(!All<TextBlock>(prompt).Any(text => text.FontFamily.Name == "Consolas"),
            "the exit prompt explains the risk without printing the game score card");
        prompt.Close();

        var notice = new NoticeWindow("Dark", "No roster to export", "Load or create a roster before exporting.", warning: true);
        notice.Show();
        notice.UpdateLayout();
        var noticeButtons = All<Button>(notice).ToArray();
        Check(noticeButtons.Length == 1 && (noticeButtons[0].Content as string) == "OK",
            "messages open in the app's own window with a single OK action");
        notice.Close();

        var confirm = new ConfirmWindow("Dark", "DELETE ROSTER", "Delete \"Visitors squad\"?", "Delete roster", destructive: true);
        confirm.Show();
        confirm.UpdateLayout();
        Check(ButtonByContent(confirm, "Delete roster") is not null && ButtonByContent(confirm, "Cancel") is not null,
            "destructive actions ask in the app's own themed dialog");
        confirm.Close();

        var picker = new ColorPickerWindow("#FF38D7A3");
        picker.Show();
        picker.UpdateLayout();
        Check(All<Button>(picker).Any(button => (button.ToolTip as string) is not null), "the colour picker offers palette swatches");
        picker.Close();
        Console.WriteLine("PASS: dialogs are the app's own themed windows");
    }

    static void ScreenFitChecks()
    {
        // The pure fit arithmetic at every size in the support matrix. The design window is the
        // main console: 1520×980 requested, 900×600 minimum.
        static void Fits(double width, double height, double expectedWidth, double expectedHeight,
            double expectedMinWidth, double expectedMinHeight)
        {
            var fit = ScreenFit.ComputeFit(new Size(width, height), new Size(900, 600), new Size(1520, 980));
            Check(fit.MaxWidth == width && fit.MaxHeight == height
                && fit.Width == expectedWidth && fit.Height == expectedHeight
                && fit.MinWidth == expectedMinWidth && fit.MinHeight == expectedMinHeight,
                $"a {width:0}×{height:0} work area fits the console to {expectedWidth:0}×{expectedHeight:0}");
        }

        Fits(1920, 1080, 1520, 980, 900, 600);
        Fits(1600, 900, 1520, 900, 900, 600);
        Fits(1366, 768, 1366, 768, 900, 600);
        Fits(2560, 1080, 1520, 980, 900, 600);
        // 150% scaling is the common laptop case: 1920×1080 and 1280×720 arrive here as the
        // logical sizes 1280×720 and 853×480.
        Fits(1280, 720, 1280, 720, 900, 600);
        Fits(1024, 600, 1024, 600, 900, 600);
        Fits(853, 480, 853, 480, 853, 480);
        Fits(1080, 1920, 1080, 980, 900, 600);

        var auto = ScreenFit.ComputeFit(new Size(853, 480), new Size(900, 600), new Size(double.NaN, double.NaN));
        Check(double.IsNaN(auto.Width) && double.IsNaN(auto.Height) && auto.MaxWidth == 853 && auto.MaxHeight == 480,
            "an auto-sized window keeps its auto size and is capped by the maximum bounds");

        // The checkpoint: a real main window on a simulated 853×480 work area must be able to
        // shrink into it rather than staying at its 900 px design minimum.
        var main = new MainWindow(LaunchMode.New) { Width = 1520, Height = 980 };
        ScreenFit.ApplyTo(main, new Size(853, 480), maximized: false);
        Check(main.MinWidth <= 853 && main.MinHeight <= 480,
            "the main window's minimums clamp down into an 853×480 work area");
        Check(main.Width <= 853 && main.Height <= 480,
            "the main window's size is capped to an 853×480 work area");
        main.Close();
        Console.WriteLine("PASS: windows fit small, portrait and scaled work areas");
    }

    /// <summary>
    /// The support matrix as a live layout contract: at every size the windows are opened at, no
    /// window outgrows its work area, header actions wrap instead of covering the brand or each
    /// other, the publish bar stays pinned, and every visible control is either on screen or inside
    /// a scroll region that can reach it. Checks are scoped to named containers so intentional
    /// overlays and hidden tabs never count as failures.
    /// </summary>
    static void ResponsiveLayoutChecks(List<string> temps)
    {
        (double Width, double Height)[] sizes =
        [
            (1920, 1080), (1600, 900), (1366, 768), (1280, 720),
            (1024, 600), (2560, 1080), (1080, 1920)
        ];

        var prefs = new UserPreferences { Theme = "Dark" };
        var engine = new GameEngine();
        engine.NewGame("football");
        // The launcher only shows its autosave list when it has a recovery point to list.
        AutosaveStore.Save(engine);
        var main = new MainWindow(LaunchMode.New) { Width = 1520, Height = 980 };
        // The real detached panel, with a program: a bare test host hides the preview strip
        // entirely, which would leave the short-window fold untested.
        var catalog = new ScorebugCatalog(TempFile(temps, ".json"));
        var session = new MediaSession(prefs);
        var panel = new GameControlWindow(engine, engine, catalog,
            () => { }, () => false, _ => { }, () => true, session, "Dark") { Width = 1360, Height = 985 };
        var launcher = new LauncherWindow();
        var settings = new SettingsWindow(prefs);
        foreach (var window in new Window[] { main, panel, launcher, settings })
        {
            window.Show();
            window.UpdateLayout();
        }

        foreach (var (width, height) in sizes)
        {
            var label = $"{width:0}×{height:0}";

            FitWindow(main, new Size(width, height), new Size(width, height));
            CheckWindowFits(main, new Size(width, height), $"the main window at {label}");
            CheckMainShell(main, width, height, label);

            FitWindow(panel, new Size(width, height), new Size(width, height));
            CheckWindowFits(panel, new Size(width, height), $"the detached panel at {label}");
            CheckDetachedPanel(panel, width, height, label);

            FitWindow(launcher, new Size(700, 580), DialogArea(width, height));
            CheckWindowFits(launcher, DialogArea(width, height), $"the launcher at {label}");
            CheckLauncher(launcher, label);

            FitWindow(settings, new Size(560, 900), DialogArea(width, height));
            CheckWindowFits(settings, DialogArea(width, height), $"preferences at {label}");
            CheckPreferences(settings, label);
        }

        // The scroll-friendly floor, and the 150%-on-720p case where the work area is smaller than
        // the window's own design minimum. This is where the rail goes compact and then overlay.
        foreach (var (width, height) in new[] { (900.0, 600.0), (853.0, 480.0) })
        {
            var label = $"{width:0}×{height:0}";
            FitWindow(main, new Size(width, height), new Size(width, height));
            CheckWindowFits(main, new Size(width, height), $"the main window at the {label} floor");
            CheckMainShell(main, width, height, label + " floor");
        }

        // Every console tab still reaches its controls at the compact floor.
        FitWindow(main, new Size(1024, 600), new Size(1024, 600));
        foreach (var (tab, host) in new[]
                 {
                     ("MediaTab", "MediaControl"), ("RosterTab", "RosterControl"),
                     ("MacrosTab", "MacroHost"), ("ControlTab", "ControlHost")
                 })
        {
            Click(main.FindName(tab) as Button);
            main.UpdateLayout();
            var surface = main.FindName(host) as Control
                ?? throw new Exception($"FAIL: the {tab} has no host");
            Check(surface.IsVisible, $"the {tab} is the visible console tab at 1024×600");
            CheckVisibleControlsUsable(surface, main, $"{tab} at 1024×600");
        }

        settings.Close();
        launcher.Close();
        panel.Close();
        session.Dispose();
        main.Close();
        Console.WriteLine("PASS: every operator window fits and stays usable across the support matrix");
    }

    /// <summary>Sizes a window through the same arithmetic a monitor change uses, then lays it out.</summary>
    static void FitWindow(Window window, Size desired, Size workArea)
    {
        window.Width = desired.Width;
        window.Height = desired.Height;
        ScreenFit.ApplyTo(window, workArea, maximized: false);
        window.UpdateLayout();
        Pump(TimeSpan.FromMilliseconds(10));
        window.UpdateLayout();
    }

    /// <summary>The work area a fixed-size dialog is fitted into, leaving ScreenFit's own margin.</summary>
    static Size DialogArea(double width, double height)
        => new(Math.Max(320, width - 2 * ScreenFit.DialogMargin),
               Math.Max(240, height - 2 * ScreenFit.DialogMargin));

    static void CheckWindowFits(Window window, Size workArea, string message)
    {
        Check(window.Width <= workArea.Width + Slack && window.Height <= workArea.Height + Slack
              && window.Bounds.Width <= workArea.Width + 2 && window.Bounds.Height <= workArea.Height + 2,
            message + " never opens wider or taller than the work area");
        Check(window.MinWidth <= workArea.Width + Slack && window.MinHeight <= workArea.Height + Slack,
            message + " can shrink into the work area");
        CheckVisibleControlsUsable(window, window, message);
    }

    /// <summary>
    /// Every visible button, picker and field has a real hit area, and anything outside the window
    /// is inside a scroller that can actually reach it. Controls inside a scroll bar itself are
    /// skipped: those are chrome, not operator actions.
    /// </summary>
    static void CheckVisibleControlsUsable(Visual root, Window viewport, string label)
    {
        var width = viewport.Bounds.Width;
        var height = viewport.Bounds.Height;
        var problems = new List<string>();
        foreach (var control in VisibleInteractive(root))
        {
            var name = Describe(control);
            // The media desk's scrub bar uses transparent RepeatButtons as pointer targets; they
            // may legitimately be empty when nothing is on air, so they are not operator actions.
            if (control is not RepeatButton && (control.Bounds.Width <= 0 || control.Bounds.Height <= 0))
                problems.Add($"{name} has no hit area");
            var rect = RectIn(control, viewport);
            if ((rect.Top < -Slack || rect.Bottom > height + Slack) && !HasScroller(control, vertical: true))
                problems.Add($"{name} is off screen without a vertical scroller");
            if ((rect.Left < -Slack || rect.Right > width + Slack) && !HasScroller(control, vertical: false))
                problems.Add($"{name} is off screen without a horizontal scroller");
        }
        CheckNoProblems(problems, $"{label}: every visible control is on screen or in a scroller");
    }

    static void CheckMainShell(MainWindow main, double width, double height, string label)
    {
        var top = Named<Border>(main, "TopBar");
        var topGrid = (Grid)top.Child!;
        var brand = topGrid.Children[0];
        var actions = (Panel)topGrid.Children[1];
        CheckNoOverlap(brand, actions, top, $"{label}: top-bar actions never cover the brand");
        CheckButtonsInside(actions, top, $"{label}: top-bar actions stay inside their bar");
        CheckButtonOverlaps(actions, $"{label}: top-bar buttons wrap instead of overlapping");

        var second = Named<Border>(main, "SecondBar");
        var secondGrid = (Grid)second.Child!;
        CheckNoOverlap(secondGrid.Children[0], secondGrid.Children[1], second,
            $"{label}: setup actions never cover the game fields");
        var setup = (Panel)secondGrid.Children[1];
        CheckButtonsInside(setup, second, $"{label}: setup actions stay inside their bar");
        CheckButtonOverlaps(setup, $"{label}: setup buttons wrap instead of overlapping");

        // A short window scrolls the whole workspace; a tall one lets the star rows fill it.
        var scrolling = Named<ScrollViewer>(main, "WorkspaceScroller").VerticalScrollBarVisibility
                        != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        Check(scrolling == (height < 900),
            $"{label}: the workspace {(height < 900 ? "scrolls in a short window" : "fills a tall window")}");

        // The rail narrows before it overlaps the console, and folds into a header toggle on the
        // smallest work areas rather than disappearing from reach.
        var rail = Named<Border>(main, "RailHost");
        var toggle = Named<Button>(main, "RailToggleButton");
        var railColumn = Named<Grid>(main, "Workspace").ColumnDefinitions[0];
        if (width < 860)
            Check(toggle.IsVisible && !rail.IsVisible && railColumn.Width.Value == 0,
                $"{label}: the rail folds behind its header toggle below 860 px");
        else if (width < 980)
            Check(!toggle.IsVisible && Math.Abs(railColumn.Width.Value - 240) < 0.5,
                $"{label}: the rail narrows to 240 px at the compact tier");
        else
            Check(!toggle.IsVisible && rail.IsVisible, $"{label}: the rail stays beside the console");

        CheckPublishBar(main.Panel, $"{label}: main console");
    }

    static void CheckDetachedPanel(GameControlWindow panel, double width, double height, string label)
    {
        // A narrow panel drops the duplicate audience column; a short one folds the preview behind
        // one toggle so the console and its tabs keep the room.
        Check(Named<Control>(panel, "AudiencePanel").IsVisible == (width >= 1050),
            $"{label}: the detached panel shows its audience column only on a wide window");
        Check(Named<Button>(panel, "PreviewToggle").IsVisible == (height < 760),
            $"{label}: the detached panel folds its preview on a short window");
        CheckPublishBar(panel.Panel, $"{label}: detached console");
    }

    static void CheckLauncher(LauncherWindow launcher, string label)
    {
        var problems = new List<string>();
        foreach (var name in new[] { "NewGameButton", "OpenGameButton", "AutosaveList" })
        {
            var control = Named<Control>(launcher, name);
            if (!control.IsEffectivelyVisible) problems.Add($"{name} is hidden");
            var rect = RectIn(control, launcher);
            if (rect.Left < -Slack || rect.Top < -Slack
                || rect.Right > launcher.Bounds.Width + Slack || rect.Bottom > launcher.Bounds.Height + Slack)
                problems.Add($"{name} is cropped by the window");
        }
        CheckNoProblems(problems, $"{label}: the launcher keeps its way in and its autosaves reachable");
    }

    static void CheckPreferences(SettingsWindow settings, string label)
    {
        var problems = new List<string>();
        foreach (var text in new[] { "Save", "Cancel" })
        {
            var button = ButtonByContent(settings, text)
                ?? throw new Exception($"FAIL: preferences has no {text} button");
            if (!button.IsEffectivelyVisible) problems.Add($"{text} is hidden");
            var rect = RectIn(button, settings);
            if (rect.Left < -Slack || rect.Top < -Slack
                || rect.Right > settings.Bounds.Width + Slack || rect.Bottom > settings.Bounds.Height + Slack)
                problems.Add($"{text} is cropped by the window");
        }
        CheckNoProblems(problems, $"{label}: preferences keeps its Save and Cancel actions reachable");

        // A 900 px page cannot be shown uncropped on a short screen; its scroller has to carry it.
        if (settings.Height < 900)
        {
            var scroller = All<ScrollViewer>(settings).First();
            Check(scroller.Extent.Height > scroller.Viewport.Height,
                $"{label}: preferences scroll instead of clipping a short window");
        }
    }

    static void CheckPublishBar(ControlPanelView panel, string label)
    {
        var problems = new List<string>();
        foreach (var name in new[]
                 {
                     "HornButton", "StagedText", "LayoutPrevButton", "LayoutNextButton",
                     "LiveOutputToggle", "UpdateButton"
                 })
        {
            var control = panel.FindName(name) as Control
                ?? throw new Exception($"FAIL: the console has no {name}");
            if (!control.IsEffectivelyVisible) problems.Add($"{name} is hidden");
            var rect = RectIn(control, panel);
            if (rect.Left < -Slack || rect.Top < -Slack
                || rect.Right > panel.Bounds.Width + Slack || rect.Bottom > panel.Bounds.Height + Slack)
                problems.Add($"{name} leaves the console");
        }
        CheckNoProblems(problems, $"{label}: Live and Update Output stay pinned at every size");
    }

    static void CheckNoProblems(List<string> problems, string message)
        => Check(problems.Count == 0, problems.Count == 0 ? message : $"{message}: {string.Join("; ", problems.Take(3))}");

    static void CheckNoOverlap(Visual left, Visual actions, Visual space, string message)
    {
        var leftRect = RectIn(left, space);
        foreach (var button in All<Button>(actions).Where(button => button.IsEffectivelyVisible))
            Check(!Overlaps(leftRect, RectIn(button, space)), message);
    }

    static void CheckButtonsInside(Panel actions, Visual bar, string message)
    {
        foreach (var button in All<Button>(actions).Where(button => button.IsEffectivelyVisible))
            CheckInside(button, bar, message);
    }

    static void CheckButtonOverlaps(Panel actions, string message)
    {
        var rects = actions.Children.OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .Select(button => RectIn(button, actions))
            .ToArray();
        for (var first = 0; first < rects.Length; first++)
            for (var second = first + 1; second < rects.Length; second++)
                Check(!Overlaps(rects[first], rects[second]), message);
    }

    static void CheckInside(Visual control, Visual container, string message)
    {
        var rect = RectIn(control, container);
        Check(rect.Left >= -Slack && rect.Top >= -Slack
              && rect.Right <= container.Bounds.Width + Slack
              && rect.Bottom <= container.Bounds.Height + Slack, message);
    }

    static Rect RectIn(Visual child, Visual ancestor)
    {
        var origin = child.TranslatePoint(default, ancestor)
            ?? throw new Exception("FAIL: a visible control has no position in its container");
        return new Rect(origin, child.Bounds.Size);
    }

    static bool Overlaps(Rect first, Rect second)
    {
        var overlapX = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left);
        var overlapY = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top);
        return overlapX > 0.5 && overlapY > 0.5;
    }

    static IEnumerable<Control> VisibleInteractive(Visual root)
        => root.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible)
            .Where(control => control is Button or ComboBox or TextBox or ListBox or Slider)
            .Where(control => !HasAncestor<ScrollBar>(control));

    static bool HasScroller(Visual control, bool vertical)
    {
        for (var parent = control.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is not ScrollViewer scroller) continue;
            var mode = vertical
                ? scroller.VerticalScrollBarVisibility
                : scroller.HorizontalScrollBarVisibility;
            if (mode != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled) return true;
        }
        return false;
    }

    static bool HasAncestor<T>(Visual visual) where T : Visual
    {
        for (var parent = visual.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
            if (parent is T) return true;
        return false;
    }

    static T Named<T>(Control root, string name) where T : Control
        => root.FindName(name) as T ?? throw new Exception($"FAIL: {name} is missing");

    static string Describe(Control control)
    {
        var text = control is Button button ? button.Content as string : null;
        var name = string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name;
        return string.IsNullOrEmpty(text) ? name : $"{name} ('{text}')";
    }

    const double Slack = 1.5;

    static void DialogChecks()
    {
        var filters = LinuxFileDialog.Filters("ScorePlus Game (*.spg)|*.spg|All files (*.*)|*.*");
        Check(filters.Count == 2 && filters[0].Contains("*.spg") && filters[1].EndsWith(" | *"),
            "WPF file filters become the Linux picker's filter list");
        Check(LinuxFileDialog.ApplyDefaultExtension("highlights", "spg", true).EndsWith("highlights.spg"),
            "a save path gets the dialog's default extension");
        var sources = MicrophoneFeed.ParseSources(
            "Source #0\n\tState: RUNNING\n\tName: alsa_input.pci-0000_00\n\tDescription: Headset microphone\n\n" +
            "Source #1\n\tState: IDLE\n\tName: alsa_output.pci-0000.analog-stereo.monitor\n\tDescription: Monitor of Speakers\n");
        Check(sources.Count == 1 && sources[0].Name == "Headset microphone",
            "microphone sources skip loopback monitors and keep their description");
        Console.WriteLine("PASS: native dialogs and PulseAudio microphone routing are wired");
    }

    static void PaintTextureChecks(List<string> temps)
    {
        var path = TempFile(temps, ".json");
        var catalog = new ScorebugCatalog(path);
        var element = catalog.Layout.Elements.First(item => item.Kind == ScorebugElementKind.Text);
        var constructor = typeof(PaintTextureWindow).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(ScorebugCatalog), typeof(GameEngine), typeof(ScorebugElement), typeof(string)],
            null) ?? throw new Exception("FAIL: paint window has no editable constructor");
        var window = (Window)constructor.Invoke([catalog, new GameEngine(), element, "Dark"]);
        window.Show();
        window.UpdateLayout();

        var materials = All<CheckBox>(window)
            .Where(box => (box.Content as string)?.StartsWith("Lay a material", StringComparison.Ordinal) == true)
            .Where(box => box.IsChecked != true)
            .ToArray();
        Check(materials.Length == 2, "paint & texture offers a material for both the text and the plate");
        foreach (var material in materials)
        {
            material.IsChecked = true;
            window.UpdateLayout();
        }
        // The window commits its draft on close; waiting on its debounce timer would depend on a
        // running platform clock, which the headless platform deliberately does not provide.
        window.Close();
        var painted = catalog.Layout.Elements.First(item => item.Id == element.Id);
        Check(painted.TextTexture is { Enabled: true } && painted.BackgroundTexture is { Enabled: true },
            "laying a material reaches both the text and the plate without a parent conflict");
    }

    static void ShellChecks()
    {
        var main = new MainWindow(LaunchMode.New) { Width = 1280, Height = 860 };
        main.Show();
        main.UpdateLayout();
        Check(main.Engine.State.HomeTeam.Length > 0, "the main window opens on a new game");
        Check(All<MediaDock>(main).Count() == 1, "the main window hosts exactly one media desk");
        main.ShowMediaTab(true);
        main.UpdateLayout();
        var dock = All<MediaDock>(main).First();
        Check(dock.IsVisible, "the MEDIA tab reveals the media desk");
        Check(dock.FindName("Tiles") is WrapPanel && dock.FindName("TagBox") is ComboBox,
            "the media desk carries its tiles and tag editor");
        Check(dock.FindName("PlayPauseButton") is Button { Bounds.Height: >= 28 }, "the transport buttons are full-size");
        Check(ButtonByContent(dock, "← Back") is not null, "the media browser names its way back");
        var chrome = dock.FindName("Chrome") as Border
            ?? throw new Exception("FAIL: the media desk has no chrome panel");
        dock.ApplyTheme("Light");
        dock.UpdateLayout();
        Check((chrome.Background as ISolidColorBrush)?.Color == Colors.White,
            "the media desk follows the light operator theme");
        dock.ApplyTheme("Dark");
        dock.UpdateLayout();
        Check((chrome.Background as ISolidColorBrush)?.Color == Color.FromRgb(8, 8, 8),
            "the media desk follows the dark operator theme");
        main.ShowMediaTab(false);
        main.UpdateLayout();
        Check(!dock.IsVisible, "the CONTROL PANEL tab puts the media desk away");
        Check(main.FindName("EventList") is null, "the event log is not embedded in the media desk");
        Check(All<Button>(main).Count(button => (button.ToolTip as string ?? "").StartsWith("Event log", StringComparison.OrdinalIgnoreCase)) == 1,
            "the event log keeps exactly one button in the top row");
        main.Close();
        Console.WriteLine("PASS: main window, tabs, media desk and chrome all load");
    }

    static void ControlPanelChecks()
    {
        var engine = new GameEngine();
        engine.NewGame("football");
        var published = 0;
        var panel = new GameControlWindow(engine, () => published++, _ => { }, () => true) { Width = 1280, Height = 860 };
        panel.Show();
        panel.UpdateLayout();

        foreach (var action in new[]
                 {
                     "home.score.plus1", "football.down.next", "football.down.previous",
                     "football.series.new", "football.ball.plus", "football.ball.minus",
                     "clock.toggle", "clock.minus1", "clock.plus1"
                 })
            panel.Execute(action);
        Check(engine.State.HomeScore == 1, "the control panel dispatches scoring actions");

        var keys = new KeybindSettings();
        keys.Bindings["home.score.plus2"] = "F9";
        keys.Bindings["football.series.new"] = "F10";
        panel.ApplyKeybinds(keys);
        engine.SetFootballDistance(4, 22);
        Press(panel, Key.F9);
        Press(panel, Key.F10);
        Check(engine.State.HomeScore == 3 && engine.State.Down == 1 && engine.State.YardsToGo == 10,
            "keybinds run their assigned buttons");

        var publish = (Button?)panel.Panel.FindName("UpdateButton") ?? throw new Exception("FAIL: the console has no Update Output button");
        Click(publish);
        Check(published == 1, "Update Output raises the publish request");

        var statsHost = (WrapPanel)panel.Panel.FindName("StatsHost")!;
        var statsBefore = engine.State.Stats.Count;
        var takeaways = engine.AddStat("Takeaways");
        panel.UpdateLayout();
        Check(statsHost.Children.Count == statsBefore + 1, "custom stats render as console rows");
        engine.AdjustStat(takeaways.Id, 3);
        panel.UpdateLayout();
        Check(panel.Panel.HomeScoreText == engine.State.HomeScore.ToString(),
            "the console refreshes after a stat change");

        var panelA = new GameControlWindow(engine, () => { }, _ => { }, () => true);
        var panelB = new GameControlWindow(engine, () => { }, _ => { }, () => true);
        panelA.Show();
        panelB.Show();
        panelA.UpdateLayout();
        panelB.UpdateLayout();
        var before = engine.State.HomeScore;
        panelA.Execute("home.score.plus2");
        panelA.UpdateLayout();
        panelB.UpdateLayout();
        Check(panelA.Panel.HomeScoreText == panelB.Panel.HomeScoreText && engine.State.HomeScore == before + 2,
            "two control panels stay in sync");
        panelA.Close();
        panelB.Close();

        // Steppers put add above the value and subtract below it.
        var step = new GameControlWindow(engine, () => { }, _ => { }, () => true);
        step.Show();
        step.UpdateLayout();
        var home = step.Panel.HomeScore;
        var plusRow = home.Children.OfType<UniformGrid>().First();
        var minusRow = home.Children.OfType<UniformGrid>().Last();
        var valueBox = home.Children.OfType<TextBox>().Single();
        Check(Grid.GetRow(plusRow) < Grid.GetRow(valueBox) && Grid.GetRow(valueBox) < Grid.GetRow(minusRow),
            "the score stepper stacks add above the value above subtract");
        var plusLabels = plusRow.Children.OfType<Button>().Select(button => button.Content as string).ToArray();
        Check(plusLabels.Contains("+6") && plusRow.Children.Count == minusRow.Children.Count,
            "the score stepper offers the sport's scoring buttons on both rows");

        // Clicking the value and typing overrides it. The box only accepts typing once it has
        // received focus, and headless windows have no pointer to give it, so raise that event.
        valueBox.Focus();
        valueBox.RaiseEvent(new GotFocusEventArgs { RoutedEvent = InputElement.GotFocusEvent });
        valueBox.Text = "17";
        Press(valueBox, Key.Enter);
        step.UpdateLayout();
        Check(engine.State.HomeScore == 17, "typing over the stepper value sets it exactly");

        // Team names are editable right on the console.
        var awayName = (TextBox)step.Panel.FindName("AwayName")!;
        awayName.Text = "Rovers";
        Press(awayName, Key.Enter);
        step.UpdateLayout();
        Check(engine.State.AwayTeam == "Rovers", "team names can be edited from the console");

        // Score and clocks publish live; structural edits wait for Update Output.
        var stageable = new GameEngine();
        stageable.NewGame("football");
        var publishedCopy = new GameEngine();
        publishedCopy.LoadFromState(stageable.CloneState());
        var signature = GameEngine.StagedSignature(publishedCopy.State);
        stageable.Score(true, 6);
        stageable.SetPlayClock(25);
        stageable.AdjustClock(-30);
        Check(GameEngine.StagedSignature(stageable.State) == signature,
            "scores and clocks publish live instead of staging");
        stageable.AdvanceDown();
        Check(GameEngine.StagedSignature(stageable.State) != signature,
            "down and distance wait for Update Output");

        // The ball spot has its own side, independent of possession.
        var ball = new GameEngine();
        ball.NewGame("football");
        var ballWindow = new GameControlWindow(ball, () => { }, _ => { }, () => true);
        ballWindow.Show();
        ballWindow.UpdateLayout();
        var side = All<Button>(ballWindow.Panel).FirstOrDefault(button => (button.Tag as string) == "football.ball.side")
            ?? throw new Exception("FAIL: the ball spot has no side button");
        Check((side.Content as string) == "HOME ▶", "the ball spot names the half it sits on");
        ball.SetPossession(false);
        ballWindow.UpdateLayout();
        Check((side.Content as string) == "HOME ▶", "possession does not decide which half the ball is on");
        Click(side);
        Check(!ball.State.BallOnHomeSide && (side.Content as string) == "◀ VISITORS",
            "the ball spot side button flips the half and relabels itself");

        // A typed play clock keeps following the clock down to zero.
        var clockEngine = new GameEngine();
        clockEngine.NewGame("football");
        var clockPanel = new GameControlWindow(clockEngine, () => { }, _ => { }, () => true);
        clockPanel.Show();
        clockPanel.UpdateLayout();
        var playBox = (TextBox)clockPanel.Panel.FindName("PlayClockValue")!;
        playBox.Text = "2";
        Press(playBox, Key.Enter);
        clockEngine.TogglePlayClock();
        Thread.Sleep(2150);
        clockEngine.Tick();
        clockPanel.UpdateLayout();
        Check(clockPanel.Panel.PlayClockText == "0", "a typed play clock still counts down to 0");

        clockPanel.Close();
        ballWindow.Close();
        step.Close();
        panel.Close();
        Console.WriteLine("PASS: control panel actions, keybinds, steppers, stats and clocks all behave");
    }

    static void TickerChecks()
    {
        var engine = new GameEngine();
        engine.NewGame("basketball");
        engine.SetLeagueGames([
            new LeagueGame
            {
                GameId = "ticker-a", HomeTeam = "Eagles", AwayTeam = "Tigers",
                HomeScore = 54, AwayScore = 51, PeriodLabel = "Q3", Clock = "4:12", Status = "live"
            },
            new LeagueGame
            {
                GameId = "ticker-b", HomeTeam = "Bears", AwayTeam = "Wolves",
                HomeScore = 40, AwayScore = 38, PeriodLabel = "Q2", Clock = "1:03", Status = "live"
            }
        ]);
        var window = new TickerWindow(engine, "Dark");
        window.Show();
        window.UpdateLayout();
        var news = All<TextBox>(window).FirstOrDefault(box => box.AcceptsReturn)
            ?? throw new Exception("FAIL: ticker rotation has no news box");
        news.Text = "League finals tonight";
        Click(ButtonByContent(window, "Save"));
        Check(engine.State.TickerGamesChosen
              && engine.State.TickerGameIds.SequenceEqual(["ticker-a", "ticker-b"])
              && engine.State.TickerNews.SequenceEqual(["League finals tonight"]),
            "ticker rotation keeps the chosen games and headlines");
        window.Close();
    }

    static void LeagueCloudChecks()
    {
        var preferences = new UserPreferences { Theme = "Dark" };
        var engine = new GameEngine();
        engine.NewGame("football");
        engine.SetTeams("Cloud Home", "Cloud Away");
        engine.SetLeagueGames([new LeagueGame
        {
            GameId = "cloud-1",
            HomeTeam = "Riverside",
            AwayTeam = "Northside",
            HomeScore = 21,
            AwayScore = 14,
            Period = 3,
            Clock = "07:42"
        }]);
        var service = new LeagueCloudService(engine, preferences);
        var catalog = new ScorebugCatalog(TempFile([], ".json"));
        var program = new ProgramSelection(catalog);
        var window = new LeagueCloudWindow(service, preferences, catalog, program, engine) { Width = 700, Height = 700 };
        window.Show();
        window.UpdateLayout();
        var games = All<ListBox>(window).Single();
        Check(games.Items.Count == 1, "the league cloud window lists games already received from the feed");
        games.SelectedIndex = 0;
        var import = ButtonByContent(window, "Import selected fields")
            ?? throw new Exception("FAIL: the league cloud window has no import action");
        Check(All<CheckBox>(window).Any(box => (box.Content as string) == "Home score")
              && All<CheckBox>(window).Any(box => (box.Content as string) == "Game clock"),
            "cloud import lets an operator choose individual score and clock fields");
        foreach (var label in new[] { "Home score", "Game clock" })
            (All<CheckBox>(window).First(box => (box.Content as string) == label)).IsChecked = true;
        Click(import);
        Check(engine.State.HomeScore == 21 && engine.State.ClockSeconds == 462,
            "importing a cloud game writes only the ticked fields");
        Check(ButtonByContent(window, "Add league ticker layout") is not null,
            "the cloud window can create a league ticker layout");
        Click(ButtonByContent(window, "Add league ticker layout"));
        Check(catalog.Layouts.Any(layout => layout.Name == "League Ticker")
              && catalog.Layout.Elements.Any(element => element.Ticker && element.Content == "{{LEAGUE_GAMES}}"),
            "the created league ticker is a real layout bound to the cloud feed");
        Check(LeagueCloudImport.TryParseClock("07:42", out var seconds) && seconds == 462,
            "league cloud clock strings are parsed for import");
        window.Close();
        service.Dispose();
    }

    static void MacroChecks(List<string> temps)
    {
        var engine = new GameEngine();
        engine.NewGame("football");
        var session = new MediaSession(new UserPreferences());
        var catalog = new ScorebugCatalog(TempFile(temps, ".json"));
        var published = 0;
        var dock = new MacroDock(engine, session, "Dark", null, catalog, () => published++);
        var host = new Window { Width = 1100, Height = 760, Content = dock };
        host.Show();
        host.UpdateLayout();
        Check(ButtonByContent(dock, "+ New macro") is not null, "the macro desk opens with a new-macro action");
        var macro = new SavedMacro
        {
            Label = "Touchdown",
            Actions = [new MacroAction { Kind = MacroActionKind.Score, Home = true, Amount = 6, BypassOutput = true }]
        };
        engine.StoreMacros([macro]);
        dock.RefreshView(force: true);
        host.UpdateLayout();
        var run = All<Button>(dock).FirstOrDefault(button => button.Tag is SavedMacro saved && saved.Id == macro.Id)
            ?? throw new Exception("FAIL: the macro board did not draw its saved card");
        Click(run);
        Check(engine.State.HomeScore == 6 && published == 1,
            "a macro card runs its ordered steps and publishes a bypassed change");
        host.Close();
        session.Dispose();
    }

    static void StudioChecks(List<string> temps)
    {
        var engine = new GameEngine();
        engine.NewGame("football");
        var path = TempFile(temps, ".json");
        var catalog = new ScorebugCatalog(path);
        var studio = new ScorebugEditorWindow(catalog, engine) { Width = 1500, Height = 900 };
        studio.Show();
        studio.UpdateLayout();
        Check(studio.Title == "ScorePlus — Display Studio"
              && All<TextBlock>(studio).Any(text => (text.Text ?? "").Contains("DISPLAY STUDIO", StringComparison.Ordinal)),
            "the studio window and its header call it Display Studio");

        var layers = (ListBox)studio.FindName("Layers")!;
        var nameInput = (TextBox)studio.FindName("NameInput")!;
        var widthInput = (TextBox)studio.FindName("WidthInput")!;
        Check(layers.Items.Count == catalog.Layout.Elements.Count,
            "the studio lists one layer per element in the active layout");

        layers.SelectedIndex = 2;
        studio.UpdateLayout();
        var selectedName = nameInput.Text;
        var selectedWidth = widthInput.Text;
        if (string.IsNullOrEmpty(selectedName)) throw new Exception("FAIL: selecting a layer did not load its properties");

        engine.Score(true, 2);
        engine.AdvanceDown();
        studio.UpdateLayout();
        Check(layers.SelectedIndex == 2 && nameInput.Text == selectedName && widthInput.Text == selectedWidth,
            "a live value update never changes the studio's selection or properties");

        var selectedIndex = catalog.Layout.Elements.Count - 1 - 2;
        nameInput.Text = "Renamed away team";
        Press(nameInput, Key.Enter);
        studio.UpdateLayout();
        var edited = catalog.Layout.Elements[selectedIndex];
        Check(edited.Name == "Renamed away team" && edited.Width.ToString("0") == selectedWidth,
            "a studio edit lands on the selected element only");
        Check(layers.SelectedIndex == 2, "the studio keeps the element selected after an edit");

        // Multi-select and Delete, in a fresh studio so no earlier selection is still held.
        var multiCatalog = new ScorebugCatalog(TempFile(temps, ".json"));
        var multiStudio = new ScorebugEditorWindow(multiCatalog, engine);
        multiStudio.Show();
        multiStudio.UpdateLayout();
        var multiLayers = (ListBox)multiStudio.FindName("Layers")!;
        Check(multiLayers.SelectionMode.HasFlag(SelectionMode.Multiple), "the layer list can hold more than one selection");
        var before = multiCatalog.Layout.Elements.Count;
        multiLayers.SelectedItems!.Add(multiLayers.Items[0]!);
        multiLayers.SelectedItems!.Add(multiLayers.Items[1]!);
        multiStudio.UpdateLayout();
        Check(multiLayers.SelectedItems!.Count == 2, "the layer list keeps a multi-selection");
        Press(multiStudio, Key.Delete);
        multiStudio.UpdateLayout();
        Check(multiCatalog.Layout.Elements.Count == before - 2, "Delete removes every selected object");
        multiStudio.Close();

        // New artwork starts transparent, and a property change reaches the whole selection.
        Click(ButtonByContent(studio, "TEXT +"));
        studio.UpdateLayout();
        Check(catalog.Layout.Elements[^1].TransparentBackground, "new studio artwork starts without a background box");
        Click(ButtonByContent(studio, "TEXT +"));
        studio.UpdateLayout();
        layers.SelectedItems!.Clear();
        foreach (var row in layers.Items.Cast<object>().Take(2)) layers.SelectedItems!.Add(row);
        studio.UpdateLayout();
        var transparent = (CheckBox)studio.FindName("TransparentBackgroundInput")!;
        transparent.IsChecked = false;
        var backgroundInput = (TextBox)studio.FindName("BackgroundInput")!;
        backgroundInput.Text = "#FF123456";
        // Enter flushes the property panel immediately; its debounce timer has no headless clock.
        Press(backgroundInput, Key.Enter);
        studio.UpdateLayout();
        var group = catalog.Layout.Elements.TakeLast(2).ToArray();
        Check(group.All(element => !element.TransparentBackground && element.Background == "#FF123456"),
            "one property change lands on every selected element");

        // The backdrop resets to navy; the old HTML page export is gone.
        Click(ButtonByContent(studio, "Navy"));
        Check(string.Equals(catalog.Layout.Background, "#FF091018", StringComparison.OrdinalIgnoreCase),
            "the artwork backdrop resets to ScorePlus navy");
        Check(ButtonByContent(studio, "Green screen") is null && ButtonByContent(studio, "HTML page…") is null,
            "the studio no longer offers the green screen or HTML page export");

        // Layouts are pictures with names; the strip switches the design.
        catalog.AddLayout("Slideshow");
        studio.UpdateLayout();
        var strip = (StackPanel)studio.FindName("LayoutStrip")!;
        Check(strip.Children.Count == 2, "the studio shows one tile per layout");
        var secondTile = (Button)strip.Children[1];
        Check(All<TextBlock>(secondTile).Any(text => (text.Text ?? "").Contains("Slideshow", StringComparison.Ordinal)),
            "a layout tile carries its name");
        Click(secondTile);
        studio.UpdateLayout();
        Check(catalog.ActiveIndex == 1, "the layout strip switches the design");
        Check(((Canvas)studio.FindName("DesignCanvas")!).Width == catalog.Layout.Width,
            "the studio canvas matches the chosen layout's size");
        catalog.SelectLayout(0);
        studio.UpdateLayout();

        // Only properties that apply are shown.
        var textIndex = catalog.Layout.Elements.FindIndex(item => item.Kind == ScorebugElementKind.Text);
        layers.SelectedIndex = catalog.Layout.Elements.Count - 1 - textIndex;
        studio.UpdateLayout();
        Check(!((Control)studio.FindName("LogoSection")!).IsVisible, "a text element is not offered the team-logo selector");
        Check(((Control)studio.FindName("IconSection")!).IsVisible, "a text element can use icon artwork");
        var gradientChoices = (ComboBox)studio.FindName("GradientSourceInput")!;
        Check(gradientChoices.Items.Count == 5, "a gradient's second colour offers all five palette slots");
        Check(ButtonByContent(studio, "Move to…") is not null && ButtonByContent(studio, "Copy to…") is not null,
            "the studio can move or copy a layout to another display");

        // Canvas presets scale the artwork and persist.
        var firstX = catalog.Layout.Elements[0].X;
        ((ComboBox)studio.FindName("CanvasPreset")!).SelectedIndex = 1; // Full HD
        Pump(TimeSpan.FromMilliseconds(400));
        Check(catalog.Layout.Width == 1920 && catalog.Layout.Height == 1080, "the Full HD preset resizes the layout");
        Check(Math.Abs(catalog.Layout.Elements[0].X - firstX * 1.5) <= 0.6, "a canvas resize scales element positions");
        Check(((Canvas)studio.FindName("DesignCanvas")!).Width == 1920, "the studio canvas adopts the new size");
        ((ComboBox)studio.FindName("CanvasPreset")!).SelectedIndex = 3; // 4K
        Pump(TimeSpan.FromMilliseconds(400));
        Check(catalog.Layout.Width == 3840 && catalog.Layout.Height == 2160, "the 4K preset resizes the layout");
        Check(catalog.Layout.Elements.All(item => item.X + item.Width <= catalog.Layout.Width + 0.5
            && item.Y + item.Height <= catalog.Layout.Height + 0.5), "4K scaling keeps every element inside the canvas");
        var reloaded = new ScorebugCatalog(path);
        Check(reloaded.Layout.Width == 3840, "the canvas size persists in the saved layout");

        // The studio backdrop is artwork, not operator chrome, in both themes.
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var themed = new ScorebugEditorWindow(reloaded, engine, theme: theme);
            themed.Show();
            themed.UpdateLayout();
            var canvas = (Canvas)themed.FindName("DesignCanvas")!;
            Check((canvas.Background as ISolidColorBrush)?.Color == Color.FromRgb(9, 16, 24),
                $"the studio canvas keeps its navy artwork backdrop in the {theme} theme");
            themed.Close();
        }

        // Fonts, text transitions and team colour slots reach the canvas. The installed families
        // differ by OS (and headless has a stub set), so the portable contract is a usable,
        // non-repeating catalogue that the picker actually binds — not a count of system fonts.
        Check(FontCatalog.All.Count > 0
              && FontCatalog.All.Select(font => font.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() == FontCatalog.All.Count,
            "the studio offers a non-empty, non-repeating font catalogue");
        Check(((ComboBox)studio.FindName("FontInput")!).Items.Count == FontCatalog.All.Count,
            "the font picker lists every catalogue entry");
        var stylePath = TempFile(temps, ".json");
        var styleCatalog = new ScorebugCatalog(stylePath);
        styleCatalog.Update(layout =>
        {
            layout.Elements.Clear();
            layout.Elements.Add(new ScorebugElement
            {
                Name = "Styled", Content = "{{HOME_TEAM}}", X = 10, Y = 10, Width = 420, Height = 90, FontSize = 48,
                ForegroundSource = ScorebugColorSource.HomePrimary, Foreground = "#FF000000"
            });
        });
        var styleEngine = new GameEngine();
        styleEngine.NewGame("football");
        styleEngine.SetTeamIdentity(true, "Home Town", "HT", null, "#FF3366CC", "#FF223344");
        var styleStudio = new ScorebugEditorWindow(styleCatalog, styleEngine);
        styleStudio.Show();
        styleStudio.UpdateLayout();
        var styledText = All<ScorebugTextBlock>((Canvas)styleStudio.FindName("DesignCanvas")!)
            .FirstOrDefault(text => text.Text == "Home Town")
            ?? throw new Exception("FAIL: the studio did not paint the team colour on open");
        Check((styledText.Foreground as ISolidColorBrush)?.Color == Color.FromRgb(0x33, 0x66, 0xCC),
            "the studio paints a team colour slot immediately, without waiting for a click");
        ((ListBox)styleStudio.FindName("Layers")!).SelectedIndex = 0;
        styleStudio.UpdateLayout();
        ((ComboBox)styleStudio.FindName("TransitionInput")!).SelectedItem = ScorebugTransition.WheelUp;
        ((CheckBox)styleStudio.FindName("ItalicInput")!).IsChecked = true;
        ((CheckBox)styleStudio.FindName("UnderlineInput")!).IsChecked = true;
        ((CheckBox)styleStudio.FindName("StrikeInput")!).IsChecked = true;
        styleStudio.UpdateLayout();
        var styled = styleCatalog.Layout.Elements[0];
        Check(styled.Transition == ScorebugTransition.WheelUp && styled.Italic && styled.Underline && styled.StrikeThrough,
            "text transitions, italic, underline and strike save to the layout");
        styledText = All<ScorebugTextBlock>((Canvas)styleStudio.FindName("DesignCanvas")!)
            .First(text => text.Text == "Home Town");
        Check(styledText.TextDecorations?.Count == 2,
            "underline and strike reach the studio canvas text");
        styleStudio.Close();
        Console.WriteLine("PASS: studio layers, multi-select, layouts, canvas presets and typography all behave");
    }

    static void StudioFeatureChecks(List<string> temps)
    {
        var engine = new GameEngine();
        engine.NewGame("football");

        // Undo and redo are real history, not a visual only button.
        var historyCatalog = new ScorebugCatalog(TempFile(temps, ".json"));
        var historyStudio = new ScorebugEditorWindow(historyCatalog, engine) { Width = 1500, Height = 900 };
        historyStudio.Show();
        historyStudio.UpdateLayout();
        var layers = (ListBox)historyStudio.FindName("Layers")!;
        var nameInput = (TextBox)historyStudio.FindName("NameInput")!;
        var undo = (Button)historyStudio.FindName("UndoButton")!;
        var redo = (Button)historyStudio.FindName("RedoButton")!;
        layers.SelectedIndex = 0;
        historyStudio.UpdateLayout();
        // The layer list is front-to-back, so row zero is the last element in the layout.
        var historyTarget = historyCatalog.Layout.Elements[^1];
        var historyId = historyTarget.Id;
        var originalName = historyTarget.Name;
        nameInput.Text = "Undo probe";
        Press(nameInput, Key.Enter);
        historyStudio.UpdateLayout();
        Check(undo.IsEnabled && historyTarget.Name == "Undo probe",
            "a studio edit becomes an enabled undo step");
        Click(undo);
        historyStudio.UpdateLayout();
        // Undo replaces the element list with a fresh snapshot, so follow the id rather than the
        // instance the test held before the restore.
        var afterUndo = historyCatalog.Layout.Elements.First(item => item.Id == historyId);
        Check(afterUndo.Name == originalName && redo.IsEnabled,
            "Undo restores the previous layout and enables redo");
        Click(redo);
        historyStudio.UpdateLayout();
        var afterRedo = historyCatalog.Layout.Elements.First(item => item.Id == historyId);
        Check(afterRedo.Name == "Undo probe",
            "Redo reapplies the undone studio edit");

        // The grid is a guide and Ctrl+wheel zooms the artwork without changing its size.
        var grid = (CheckBox)historyStudio.FindName("CanvasGridInput")!;
        var gridCanvas = (Canvas)historyStudio.FindName("GridCanvas")!;
        grid.IsChecked = true;
        historyStudio.UpdateLayout();
        Check(gridCanvas.IsVisible && gridCanvas.Children.Count > 0,
            "the studio grid draws guide lines when enabled");
        grid.IsChecked = false;
        historyStudio.UpdateLayout();
        Check(!gridCanvas.IsVisible, "the studio grid disappears when disabled");
        var frame = (Border)historyStudio.FindName("CanvasFrame")!;
        var viewbox = (Viewbox)historyStudio.FindName("ArtworkViewbox")!;
        var zoomText = (TextBlock)historyStudio.FindName("CanvasZoomText")!;
        var beforeZoom = viewbox.Width;
        var canvasPoint = frame.TranslatePoint(new Point(frame.Bounds.Width / 2, frame.Bounds.Height / 2), historyStudio)
            ?? new Point(600, 400);
        historyStudio.MouseWheel(canvasPoint, new Vector(0, 1), RawInputModifiers.Control);
        historyStudio.UpdateLayout();
        Check(viewbox.Width > beforeZoom && zoomText.Text == "110%",
            "Ctrl+wheel zooms the studio artwork and updates its readout");
        historyStudio.Close();

        // Ticker and player-photo buttons create the ready-made parts they advertise.
        var partsCatalog = new ScorebugCatalog(TempFile(temps, ".json"));
        var partsStudio = new ScorebugEditorWindow(partsCatalog, engine) { Width = 1500, Height = 900 };
        partsStudio.Show();
        partsStudio.UpdateLayout();
        Click(ButtonByContent(partsStudio, "TICKER +"));
        partsStudio.UpdateLayout();
        Check(partsCatalog.Layout.Elements[^1] is { Ticker: true, Kind: ScorebugElementKind.Text },
            "TICKER + adds an enabled scrolling ribbon");
        Click(ButtonByContent(partsStudio, "PLAYER PHOTO +"));
        partsStudio.UpdateLayout();
        var photo = partsCatalog.Layout.Elements[^1];
        Check(photo.Kind == ScorebugElementKind.Image && photo.PlayerPhotoSide == ScorebugPlayerPhotoSide.Home,
            "PLAYER PHOTO + adds a portrait bound to the home player");
        var photoSection = (Control)partsStudio.FindName("PlayerPhotoSection")!;
        var photoSide = (ComboBox)partsStudio.FindName("PlayerPhotoSideInput")!;
        Check(photoSection.IsVisible && (ScorebugPlayerPhotoSide?)photoSide.SelectedItem == ScorebugPlayerPhotoSide.Home,
            "the player-photo properties show and carry the chosen side");
        partsStudio.Close();

        // A live-layout element exposes its source picker, rejects the current layout and persists a change.
        var feedCatalog = new ScorebugCatalog(TempFile(temps, ".json"));
        feedCatalog.AddLayout("Source feed");
        var sourceLayout = feedCatalog.Layouts[1];
        feedCatalog.SelectLayout(0);
        feedCatalog.Update(layout =>
        {
            layout.Elements.Clear();
            layout.Elements.Add(new ScorebugElement
            {
                Name = "Live feed", Kind = ScorebugElementKind.Layout, NestedLayoutId = sourceLayout.Id,
                X = 20, Y = 20, Width = 400, Height = 225, Content = ""
            });
        });
        var feedStudio = new ScorebugEditorWindow(feedCatalog, engine) { Width = 1500, Height = 900 };
        feedStudio.Show();
        feedStudio.UpdateLayout();
        ((ListBox)feedStudio.FindName("Layers")!).SelectedIndex = 0;
        feedStudio.UpdateLayout();
        var feedSection = (Control)feedStudio.FindName("LayoutSection")!;
        var sourcePicker = (ComboBox)feedStudio.FindName("NestedLayoutInput")!;
        Check(feedSection.IsVisible, "a live-layout element shows the live-layout properties");
        Check(ButtonByContent(feedStudio, "LAYOUT +") is not null && sourcePicker.ItemCount == 2,
            "the studio offers LAYOUT + and only legal live-layout sources");
        sourcePicker.SelectedIndex = 0;
        feedStudio.UpdateLayout();
        Check(feedCatalog.Layout.Elements[0].NestedLayoutId is null,
            "choosing no source removes the live-layout feed");
        sourcePicker.SelectedIndex = 1;
        feedStudio.UpdateLayout();
        Check(feedCatalog.Layout.Elements[0].NestedLayoutId == sourceLayout.Id,
            "choosing a source stores the live-layout feed");
        feedStudio.Close();

        // Score-video rows are rendered from the layout and their remove button edits it.
        var videoCatalog = new ScorebugCatalog(TempFile(temps, ".json"));
        videoCatalog.Update(layout => layout.ScoreVideos.Add(new ScoreVideoRule
        {
            Amount = 6,
            Video = new ScoreVideo { Path = "/tmp/touchdown.mp4", ChromaKeyColor = "#FF00B140", ChromaKeyTolerance = 0.2 }
        }));
        var videoStudio = new ScorebugEditorWindow(videoCatalog, engine) { Width = 1500, Height = 900 };
        videoStudio.Show();
        videoStudio.UpdateLayout();
        var videoRows = (StackPanel)videoStudio.FindName("ScoreVideoRows")!;
        var videoEmpty = (Control)videoStudio.FindName("ScoreVideoEmptyText")!;
        Check(videoRows.Children.Count == 1 && videoCatalog.Layout.ScoreVideos.Count == 1,
            "a saved score video produces one studio row");
        Check(All<TextBlock>(videoRows).Any(text => (text.Text ?? "").Contains("+6", StringComparison.Ordinal)
                && (text.Text ?? "").Contains("touchdown.mp4", StringComparison.Ordinal)),
            "the score-video row names its score change and clip");
        Check(All<Button>(videoRows).Any(button => (button.Content as string) == "Keyed")
              && All<Button>(videoRows).Any(button => (button.Content as string) == "Video…"),
            "a keyed score video offers key status and clip replacement");
        Click(ButtonByContent(videoRows, "×"));
        videoStudio.UpdateLayout();
        Check(videoCatalog.Layout.ScoreVideos.Count == 0 && videoRows.Children.Count == 0 && videoEmpty.IsVisible,
            "removing a score-video row deletes the rule and restores the empty hint");
        videoStudio.Close();

        Console.WriteLine("PASS: studio undo, zoom, grid, ticker, player photo, live layout and score videos behave");
    }

    static void WindowCatalogChecks(List<string> temps)
    {
        var bindings = new KeybindSettings();
        var keybinds = new KeybindsWindow(bindings);
        keybinds.Show();
        keybinds.UpdateLayout();
        var keybindList = All<ListBox>(keybinds).Single();
        Check(keybindList.Items.Count == ControlActions.ForSport(null).Count,
            "the keybinds window lists every bindable action for the shared console");
        keybinds.Close();

        var baseball = ControlActions.ForSport("baseball").Select(action => action.Id).ToArray();
        Check(!baseball.Contains("football.down.next") && !baseball.Contains("soccer.red.home"),
            "baseball keybinds do not offer another sport's buttons");
        Check(baseball.Contains("baseball.ball") && baseball.Contains("home.score.plus1") && baseball.Contains("output.publish"),
            "baseball keybinds carry its own and the shared buttons");
        Check(ControlActions.ForSport("football").Any(action => action.Id == "football.playclock.toggle"),
            "football keybinds keep the play clock");

        var text = KeybindStore.ToText(Key.D2, KeyModifiers.Control);
        var matched = KeybindStore.Match(bindings, Key.D2, KeyModifiers.Control);
        bindings.Bindings["home.score.plus2"] = text;
        Check(KeybindStore.Match(bindings, Key.D2, KeyModifiers.Control) == "home.score.plus2"
              && KeybindStore.TryParse(text, out var parsedKey, out var parsedMods)
              && parsedKey == Key.D2 && parsedMods == KeyModifiers.Control,
            "keybind text round trips through the store");

        var sports = new SportCatalog(TempFile(temps, ".json"));
        var picker = new SportPickerWindow(sports, "hockey", "Dark");
        picker.Show();
        picker.UpdateLayout();
        var choices = All<ListBox>(picker).First();
        Check(choices.Items.Count == sports.All.Count, "a new game lists every sport it can be played in");
        Check(choices.SelectedItem is not null, "the sport chooser opens with a sport selected");
        picker.Close();

        var profileCatalog = new SportCatalog(TempFile(temps, ".json"));
        var profile = new SportProfileWindow(profileCatalog);
        profile.Show();
        profile.UpdateLayout();
        var profiles = (ListBox)profile.FindName("Profiles")!;
        profiles.SelectedItem = profiles.Items.Cast<object>().First(item => (item.ToString() ?? "").Contains("American Football", StringComparison.Ordinal));
        profile.UpdateLayout();
        ((TextBox)profile.FindName("TimeoutsInput")!).Text = "6";
        ((TextBox)profile.FindName("ScoringInput")!).Text = "2, 3, 7";
        Click((Button)profile.FindName("SaveButton")!);
        var saved = profileCatalog.Find("football")!;
        Check(saved.Timeouts == 3 && saved.ScoringOptions.SequenceEqual([2, 3, 7]) && profileCatalog.IsOverridden("football"),
            "editing a built-in sport profile clamps football timeouts and saves scoring buttons");
        profile.Close();

        var logEngine = new GameEngine();
        logEngine.NewGame("football");
        logEngine.Score(true, 3);
        var eventLog = new EventLogWindow(logEngine, "Dark", false);
        eventLog.Show();
        eventLog.UpdateLayout();
        var logList = (ListBox)eventLog.FindName("LogList")!;
        Check(eventLog.IsVisible && logList.Items.Count == logEngine.State.Events.Count && logList.Items.Count > 0,
            "the event log shows every game event in its own window");
        eventLog.Close();

        var identity = new TeamIdentityWindow(new GameEngine(), true, "Dark");
        identity.Show();
        identity.UpdateLayout();
        Check(All<TextBox>(identity).Count() == 4, "the team details box edits name, abbreviation and both colours");
        identity.Close();
        Console.WriteLine("PASS: keybind, sport, profile, event-log and team-detail windows all load");
    }

    static void MediaBackendChecks(List<string> temps)
    {
        Check(MediaProbe.VideoStatus.Length > 0 && MediaProbe.AudioStatus.Length > 0,
            "the media probe names the graphics and audio tools it found");
        if (!MediaProbe.HasVideo) { Console.WriteLine("SKIP: ffmpeg is not installed for the video backend checks"); return; }

        var clip = TempFile(temps, ".mp4");
        using (var make = Process.Start(MediaProbe.Start(MediaProbe.Ffmpeg,
            "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=10",
            "-t", "2", "-pix_fmt", "yuv420p", clip)))
            make?.WaitForExit(8000);
        Check(File.Exists(clip) && new FileInfo(clip).Length > 1000, "the test clip is generated by ffmpeg");

        using var player = new MediaPlayer { Volume = 0 };
        var opened = false;
        player.MediaOpened += (_, _) => opened = true;
        player.Open(new Uri(clip));
        Pump(TimeSpan.FromMilliseconds(350));
        Check(opened && player.NaturalDuration.HasTimeSpan && player.NaturalDuration.TimeSpan.TotalSeconds >= 1.5,
            "the transport reads a clip's duration through ffprobe");
        player.Play();
        Pump(TimeSpan.FromMilliseconds(650));
        Check(player.Position.TotalSeconds > 0.15, "the transport clock advances while a clip plays");
        player.Close();

        var surface = new MediaElement { Volume = 0 };
        var ended = false;
        surface.MediaEnded += (_, _) => ended = true;
        var host = new Window { Width = 320, Height = 180, Content = surface };
        host.Show();
        host.UpdateLayout();
        surface.Source = new Uri(clip);
        surface.Play();
        Pump(TimeSpan.FromMilliseconds(900));
        Check(surface.CurrentFrame is not null, "ffmpeg frames reach the Avalonia image surface");
        Check(!ended, "a two-second cue is still playing after less than a second");
        Pump(TimeSpan.FromMilliseconds(800));
        Check(!ended, "a two-second cue is still playing before its duration is up");
        Pump(TimeSpan.FromMilliseconds(700));
        Check(ended, "a two-second cue ends after its frames have played");
        surface.Pause();
        surface.Close();
        host.Close();
        Console.WriteLine("PASS: ffmpeg video, ffprobe duration and the transport clock all work");
    }

    static void PortedFeatureChecks(List<string> temps)
    {
        var adPath = TempFile(temps, ".json");
        AdLogStore.UsePath(adPath);
        AdLogStore.Count(Path.Combine(temps.Count == 0 ? "" : Path.GetTempPath(), "sponsor.mp4"), "Sponsor spot", 12);
        Check(AdLogStore.All().Count == 1 && AdLogStore.TotalPlays == 1,
            "the ad log records one counted impression");
        var ad = new AdLogWindow("Dark");
        ad.Show();
        ad.UpdateLayout();
        Pump(TimeSpan.FromMilliseconds(80));
        ad.UpdateLayout();
        Check(All<TextBlock>(ad).Any(text => (text.Text ?? "").Contains("ADVERTISEMENT LOG", StringComparison.Ordinal))
              && All<TextBlock>(ad).Any(text => text.Text == "Sponsor spot"),
            "the ad log lists the counted clip");
        ad.Close();
        AdLogStore.Reset();
        AdLogStore.UsePath(null);

        var catalogPath = TempFile(temps, ".json");
        var catalog = new ScorebugCatalog(catalogPath);
        var engine = new GameEngine();
        engine.NewGame("football");
        var service = new HardwareService(engine);
        var hardware = new HardwareWindow(new UserPreferences(), service);
        hardware.Show();
        hardware.UpdateLayout();
        Check(All<TextBlock>(hardware).Any(text => text.Text == "HARDWARE")
              && All<ComboBox>(hardware).Count() >= 3, "the hardware window offers controller, transport and port pickers");
        hardware.Close();
        service.Dispose();

        var automation = new AutomationWindow(catalog, engine, "Dark", null, null);
        automation.Show();
        automation.UpdateLayout();
        automation.AddRule();
        Check(automation.Rules.Count > 0, "the conditionals window can create a rule");
        automation.Close();

        var icons = new IconMakerWindow("Dark");
        icons.Show();
        icons.UpdateLayout();
        Check(All<TextBlock>(icons).Any(text => (text.Text ?? "").Contains("ICON", StringComparison.OrdinalIgnoreCase)),
            "the icon creator opens its editing surface");
        icons.Close();

        var prefs = new UserPreferences { Theme = "Dark" };
        var session = new MediaSession(prefs);
        var playback = session.Playback;
        var program = new ProgramSelection(catalog);
        var runtime = new AutomationRuntime(catalog, engine);
        var multiview = new MultiviewWindow(session, prefs, catalog, engine, engine, program, runtime, playback);
        multiview.Show();
        multiview.UpdateLayout();
        Check(All<TextBlock>(multiview).Any(text => (text.Text ?? "").Contains("MULTIVIEW", StringComparison.OrdinalIgnoreCase)),
            "the multiview window opens with its camera wall");
        multiview.Close();
        var booth = new ReplayBoothController(session, engine, prefs);
        var replay = new ReplayBoothWindow(booth, prefs);
        replay.Show();
        replay.UpdateLayout();
        Check(All<Button>(replay).Any(button => (button.Content as string) == "Live edge"),
            "the replay booth offers its live-edge transport");
        replay.Close();
        booth.Dispose();
        runtime.Dispose();
        session.Dispose();
        program.Dispose();

        Check(OperatorLock.Matches("secret", OperatorLock.Hash("secret"))
              && !OperatorLock.Matches("wrong", OperatorLock.Hash("secret")),
            "the operator lock hashes and verifies its password");
        Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "horn-air.mp3")),
            "the horn assets ship beside the app");
        Console.WriteLine("PASS: ad log, hardware, conditionals, icons, multiview, replay booth and operator lock are reachable");
    }

    static void ThemeChecks()
    {
        // The launcher sits outside ThemeManager and is always dark. Fluent's default variant has
        // to be dark too: on a light desktop its light-theme hover foreground is near-black, so
        // the launcher's white button labels would turn black while the pointer is over them.
        var launcher = new LauncherWindow();
        launcher.Show();
        launcher.UpdateLayout();
        var newGame = ButtonByContent(launcher, "New Game")!;
        var label = newGame.GetVisualDescendants().OfType<ContentPresenter>()
            .FirstOrDefault(presenter => presenter.Name == "PART_ContentPresenter")
            ?? throw new Exception("FAIL: the launcher's primary button has no template label");
        var point = newGame.TranslatePoint(new Point(newGame.Bounds.Width / 2, newGame.Bounds.Height / 2), launcher)
            ?? throw new Exception("FAIL: the launcher's primary button has no position");
        launcher.MouseMove(point);
        Pump(TimeSpan.FromMilliseconds(50));
        Check(newGame.IsPointerOver, "the launcher's primary button reports the pointer over it");
        var hovered = label.Foreground as ISolidColorBrush;
        Check(hovered is not null && hovered.Color.R > 0xC0 && hovered.Color.G > 0xC0 && hovered.Color.B > 0xC0,
            "hovering the launcher's white button label keeps it light instead of turning it black");
        launcher.Close();
        Check(Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark,
            "the app pins Fluent to the dark variant for its unthemed dark surfaces");

        Check(ThemeManager.Resolve("System") is "Dark" or "Light", "the System theme resolves to a real theme");
        var fresh = new UserPreferences();
        Check(fresh.Theme == "System" && !fresh.ShowHeadingHints, "preferences default to System with heading tips off");
        Check(KeybindStore.Defaults().Bindings.Count == 0, "no shortcut is assigned until the operator picks one");

        var prefs = new UserPreferences { Theme = "Dark" };
        var cancel = new SettingsWindow(prefs);
        cancel.Show();
        cancel.UpdateLayout();
        var cancelTheme = (ComboBox)cancel.FindName("ThemeSelector")!;
        cancelTheme.SelectedItem = "Light";
        cancel.Close();
        Check(prefs.Theme == "Dark", "cancelling preferences leaves the theme alone");

        var settings = new SettingsWindow(prefs);
        settings.Show();
        settings.UpdateLayout();
        var theme = (ComboBox)settings.FindName("ThemeSelector")!;
        theme.SelectedItem = "Light";
        Click(ButtonByContent(settings, "Save"));
        Check(prefs.Theme == "Light", "saving preferences applies the chosen theme");
        Check((theme.ItemsSource as string[])?.Contains("System") == true, "preferences offer the System theme");
        theme.SelectedItem = "System";
        Click(ButtonByContent(settings, "Save"));
        Check(prefs.Theme == "System", "the System theme can be saved");
        settings.Close();

        // Enter in a preference field saves but leaves the page open.
        var delayPrefs = new UserPreferences { Theme = "Dark", ValueSwitchDelay = 0 };
        var delayWindow = new SettingsWindow(delayPrefs);
        delayWindow.Show();
        delayWindow.UpdateLayout();
        var delayBox = (TextBox)delayWindow.FindName("DelayInput")!;
        delayBox.Focus();
        delayBox.Text = "2.5";
        // Enter is intercepted on the window's tunnel route, so it has to enter through the
        // focused window rather than being raised on the box itself.
        delayWindow.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Check(delayWindow.IsVisible, "Enter in a preference field does not close the page");
        Check(Math.Abs(delayPrefs.ValueSwitchDelay - 2.5) < 0.001, "Enter in a preference field saves the typed value");
        delayWindow.Close();

        // A plain dialog wears the theme; designed artwork does not.
        var probe = new Window { Width = 300, Height = 200 };
        var list = new ListBox();
        probe.Content = list;
        ThemeManager.Apply(probe, "Dark");
        probe.Show();
        probe.UpdateLayout();
        Pump(TimeSpan.FromMilliseconds(150));
        Check((list.Background as ISolidColorBrush)?.Color.R <= 100, "a plain dialog picks up the dark chrome");
        probe.Close();

        var engine = new GameEngine();
        engine.NewGame("football");
        var themePath = Path.Combine(Path.GetTempPath(), $"smoke-theme-{Guid.NewGuid():N}.json");
        var catalog = new ScorebugCatalog(themePath);
        var editor = new ScorebugEditorWindow(catalog, engine);
        editor.Show();
        editor.UpdateLayout();
        var designed = All<TextBlock>((Canvas)editor.FindName("DesignCanvas")!).FirstOrDefault(text => (text.Text ?? "").Length > 0)
            ?? throw new Exception("FAIL: the studio artwork has no text to theme");
        var before = (designed.Foreground as ISolidColorBrush)?.Color;
        ThemeManager.Apply(editor, "Light");
        Pump(TimeSpan.FromMilliseconds(150));
        Check((designed.Foreground as ISolidColorBrush)?.Color == before, "a theme change never recolours scorebug artwork");
        editor.Close();
        try { File.Delete(themePath); } catch { }
        Console.WriteLine("PASS: preferences, themes and plain-dialog theming behave");
    }

    static void IconChecks(List<string> temps)
    {
        var engine = new GameEngine();
        engine.NewGame("football");
        var item = new ScorebugElement { Name = "Timeouts", Content = "", Kind = ScorebugElementKind.Text, Icon = ScorebugIcon.TimeoutBars, Width = 160, Height = 60 };
        var view = new ScorebugIconView(item, () => engine.State);
        Check(engine.State.HomeTimeouts > 0
              && All<Rectangle>(view).Count() == engine.State.HomeTimeouts,
            "the timeout icon draws one bar per timeout");
        Check(All<Rectangle>(view).All(bar => bar.Width > bar.Height * 2), "each timeout mark is a horizontal bar");
        var stack = All<StackPanel>(view).FirstOrDefault() ?? throw new Exception("FAIL: timeout bars have no stack");
        Check(stack.Orientation == global::Avalonia.Layout.Orientation.Horizontal, "timeout bars sit in a row");
        engine.Timeout(true);
        view.Update();
        var timeoutBars = All<Rectangle>(view).ToList();
        Check(engine.State.HomeTimeoutLimit > engine.State.HomeTimeouts
              && timeoutBars.Count == engine.State.HomeTimeoutLimit,
            "the timeout icon keeps the full allotment after a timeout");
        Check(timeoutBars.Skip(engine.State.HomeTimeouts)
              .All(bar => (bar.Fill as ISolidColorBrush)?.Color == Color.FromRgb(0x9A, 0xA5, 0xB2)),
            "a used timeout turns grey");

        var dotItem = new ScorebugElement { Name = "Possession", Content = "{{POSSESSION}}", Kind = ScorebugElementKind.Text, Icon = ScorebugIcon.PossessionDot, Width = 200, Height = 60, FontSize = 24 };
        var dotView = new ScorebugIconView(dotItem, () => engine.State);
        var dot = All<Ellipse>(dotView).Single();
        Check(!All<TextBlock>(dotView).Any(), "the possession dot prints no value beside the mark");
        engine.SetPossession(false);
        dotView.Update();
        Check(dot.HorizontalAlignment == global::Avalonia.Layout.HorizontalAlignment.Left,
            "the possession dot follows the team with the ball");
        engine.SetPossession(true);
        dotView.Update();
        Check(dot.HorizontalAlignment == global::Avalonia.Layout.HorizontalAlignment.Right,
            "the possession dot moves to the home side");
        var size = dot.Width;
        dotItem.Width = 500;
        dotItem.Height = 200;
        dotView.Update();
        Check(Math.Abs(dot.Width - size) < 0.01, "the possession dot ignores its box size");
        dotItem.FontSize = 48;
        dotView.Update();
        Check(dot.Width > size + 1, "the possession dot follows the element's type size");

        // Both icons draw on a real scorebug canvas.
        var catalog = new ScorebugCatalog(TempFile(temps, ".json"));
        catalog.Update(layout =>
        {
            layout.Elements.Clear();
            layout.Elements.Add(new ScorebugElement { Name = "Timeouts", Kind = ScorebugElementKind.Text, Content = "", Icon = ScorebugIcon.TimeoutBars });
            layout.Elements.Add(new ScorebugElement { Name = "Ball", Kind = ScorebugElementKind.Text, Content = "{{POSSESSION}}", Icon = ScorebugIcon.PossessionDot });
        });
        var canvas = new ScorebugCanvasView(catalog, engine);
        canvas.Measure(new Size(1280, 720));
        canvas.Arrange(new Rect(0, 0, 1280, 720));
        canvas.UpdateLayout();
        Check(All<ScorebugIconView>(canvas).Count() == 2, "the scorebug canvas draws icon elements");
        canvas.Dispose();
        Console.WriteLine("PASS: timeout bars and the possession dot follow live data");
    }

    static void VideoRoutingChecks(List<string> temps)
    {
        var engine = new GameEngine();
        engine.NewGame("football");
        var catalog = new ScorebugCatalog(TempFile(temps, ".json"));
        var playback = new MediaPlayback();
        catalog.Update(layout => layout.Elements.Add(new ScorebugElement
        {
            Name = "Ad video", Kind = ScorebugElementKind.Video, MediaTag = "Ads",
            Width = 480, Height = 270, X = 40, Y = 40, TransparentBackground = true
        }));
        var view = new ScorebugCanvasView(catalog, engine, playback: playback);
        view.Measure(new Size(640, 360));
        view.Arrange(new Rect(0, 0, 640, 360));
        view.UpdateLayout();
        var surface = All<VideoSurface>(view).SingleOrDefault() ?? throw new Exception("FAIL: no video surface was created");
        Check(surface.Clip is RectangleGeometry clip && clip.Rect.Width > 0,
            "a video surface is clipped to its element box");
        view.Dispose();

        var ad = new MediaEntry(Path.Combine("media", "ad.mp4"), "Ads");
        var highlight = new MediaEntry(Path.Combine("media", "highlight.mp4"), "Highlights");
        var anthem = new MediaEntry(Path.Combine("media", "anthem.mp3"), "Ads");
        playback.Start(ad);
        Check(playback.ActiveFor("Ads")?.Path == ad.Path, "a tagged clip reaches its matching video element");
        Check(playback.ActiveFor("Highlights") is null, "a clip does not play in the wrong tag's element");
        Check(playback.ActiveFor("")?.Path == ad.Path, "an untagged element accepts any playing video");
        playback.Start(anthem);
        Check(playback.ActiveFor("") is null, "an audio cue never fills a video element");
        playback.Start(highlight);
        Check(playback.ActiveFor("Ads") is null, "a highlight reel does not play in the ad box");
        Check(playback.Matches(ad, MediaLibrary.AnyTag) && playback.Matches(highlight, "any"),
            "the ANY tag accepts every clip");
        var camera = new MediaEntry(MediaLibrary.CameraPath(0), "Camera");
        Check(camera.IsCamera && camera.IsVideo, "a camera source counts as a live video source");
        playback.Start(camera);
        Check(playback.ActiveFor(MediaLibrary.AnyTag)?.IsCamera == true, "a live camera routes into an ANY video box");

        // A replay waits in the preview until Start replay puts it on air.
        var staging = new MediaPlayback();
        var replayClip = new MediaEntry(Path.Combine("media", "replay-0001.avi"), "Camera");
        staging.Start(camera);
        staging.Stage(replayClip);
        Check(staging.ActiveFor("Camera")?.Path == camera.Path, "a staged replay does not reach the audience cue");
        Check(staging.PreviewFor("Camera")?.Path == replayClip.Path, "the preview shows the staged replay");
        staging.PromoteStage();
        Check(staging.ActiveFor("Camera")?.Path == replayClip.Path && !staging.HasStaged,
            "Start replay puts the staged replay on air");
        staging.Start(camera);
        Check(!staging.HasStaged, "going live clears the staged replay");

        // Each clip carries its own level and speaker.
        var quiet = ad with { Volume = 0.35, AudioDevice = "Speaker B" };
        playback.Start(quiet);
        Check(Math.Abs(playback.Volume - 0.35) < 0.001 && playback.RoutedToDevice,
            "a clip's own volume and speaker reach the scorebug");
        playback.Start(ad);
        Check(!playback.RoutedToDevice && Math.Abs(playback.Volume - 1) < 0.001,
            "the next clip does not inherit the previous clip's audio");
        playback.Stop();
        Check(playback.ActiveFor("Ads") is null, "stopping media clears the overlay");

        // Microphones are live audio-only library items.
        var micPath = MediaLibrary.MicrophonePath(1);
        var mic = new MediaEntry(micPath, "Microphone");
        Check(mic.IsMicrophone && !mic.IsVideo && mic.KindLabel == "MIC", "a microphone is an audio-only live item");
        Check(MediaLibrary.MicrophoneIndex(micPath) == 1 && MediaLibrary.CameraIndex(micPath) is null,
            "a microphone path round trips");
        Console.WriteLine("PASS: video tags, ANY, cameras, replays, per-clip audio and microphones all route correctly");
    }

    static void MediaChecks(List<string> temps)
    {
        var library = new List<MediaEntry>
        {
            new(Path.Combine("media", "ad.mp4"), "General"),
            new(Path.Combine("media", "song.mp3"), "Music")
        };
        var retagged = MediaLibrary.Retag(library, library[0], "Ads")!;
        Check(retagged.Tag == "Ads" && library[0].Tag == "Ads", "assigning a tag updates the clip");
        Check(MediaLibrary.Retag(library, library[1], "  ")!.Tag == MediaLibrary.DefaultTag,
            "an empty tag falls back to General");
        var json = System.Text.Json.JsonSerializer.Serialize(new UserPreferences { Media = library });
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<UserPreferences>(json)!;
        Check(reloaded.Media.Count == 2 && reloaded.Media[0].Tag == "Ads" && reloaded.Media[0].IsVideo
              && reloaded.Media[1].Tag == MediaLibrary.DefaultTag,
            "the media library survives a preferences round trip");

        var session = new MediaSession(new UserPreferences());
        try
        {
            session.Library.Add(new MediaEntry(Path.Combine("media", "smoke-clear.mp4"), "Ads"));
            session.Playback.Start(new MediaEntry(Path.Combine("media", "smoke-clear.mp4"), "Ads"));
            session.ClearAll();
            Check(session.Library.Count == 0 && session.Playlists.Count == 0 && session.Playback.NowPlaying is null,
                "Clear empties the media library and blanks what was on air");
            Check(!session.IsReplaying && !session.PlayReplay(), "Start replay refuses when no replay is open");

            var adClip = new MediaEntry(Path.Combine("media", "smoke-bus.mp4"), "Ads");
            var musicClip = new MediaEntry(Path.Combine("media", "smoke-bed.mp3"), "Music");
            session.Library.Add(adClip);
            session.Library.Add(musicClip);
            session.SetClipTransition(adClip, "Fade");
            session.SetClipTransitionSeconds(adClip, 1.25);
            var tuned = session.Library.First(entry => string.Equals(entry.Path, adClip.Path, StringComparison.OrdinalIgnoreCase));
            Check(tuned.Transition == "Fade" && Math.Abs(tuned.TransitionSeconds - 1.25) < 0.001,
                "a clip carries its own switch animation and duration");
            var bus = session.AddSpeaker(0, "Ads");
            Check(bus.IsSpeaker && session.HasSpeakerBuses && Math.Abs(session.SpeakerGain(adClip) - 1) < 0.001,
                "a speaker bus carries clips with its tag");
            session.ToggleOnAir(bus);
            Check(Math.Abs(session.SpeakerGain(adClip)) < 0.001,
                "switching a speaker bus off silences its tag");
            session.ToggleOnAir(session.Selected!);
            Check(Math.Abs(session.SpeakerGain(adClip) - 1) < 0.001,
                "switching a speaker bus back on restores its tag");
            var order = session.CreatePlaylist("Smoke order");
            session.AddToPlaylist(order, adClip);
            session.AddToPlaylist(order, musicClip);
            session.MovePlaylistItem(order, 0, 1);
            Check(order.Paths.Count == 2 && string.Equals(order.Paths[0], musicClip.Path, StringComparison.OrdinalIgnoreCase),
                "a playlist clip moves up and down its play order");
            var cameraEntry = new MediaEntry(MediaLibrary.CameraPath(0), "Smoke cam");
            session.Library.Add(cameraEntry);
            session.Select(cameraEntry);
            Check(string.Equals(session.ReplaySource?.Path, cameraEntry.Path, StringComparison.OrdinalIgnoreCase),
                "ReplaySource follows the selected camera");
            session.ClearOnAir();
            Check(session.Library.Count > 0 && session.Current is null && session.Playback.NowPlaying is null,
                "Clear off air blanks the screen but keeps the library");

            var mic = session.AddMicrophone(0, "Commentary");
            Check(mic.IsMicrophone && session.Library.Contains(mic) && mic.Tag == "Commentary",
                "Add microphone files the item in the library");
            Check(ReferenceEquals(session.AddMicrophone(0), mic), "adding the same microphone twice reuses its item");
            session.RouteMicrophone(mic, 1);
            Check(session.Selected?.MicrophoneIndex == 1 && MediaLibrary.MicrophoneIndex(session.Selected?.Path ?? "") == 0,
                "routing a microphone changes its device without moving its library slot");
            session.RenameMicrophone(mic, "Desk mic");
            Check(session.Selected?.Label == "Desk mic", "renaming a microphone updates its item");
        }
        finally { session.Stop(); session.Dispose(); }

        // The dock renders the shared library, with its tags, transport and speaker picker.
        var prefs = new UserPreferences();
        var dockSession = new MediaSession(prefs);
        dockSession.Library.Add(new MediaEntry(Path.Combine("media", "smoke-ad.mp4"), "Ads"));
        try
        {
            var dock = new MediaDock();
            dock.Attach(dockSession);
            var host = new Window { Width = 460, Height = 780, Content = dock };
            host.Show();
            host.UpdateLayout();
            var tiles = (WrapPanel)dock.FindName("Tiles")!;
            tiles.UpdateLayout();
            Check(tiles.Children.OfType<Border>().Count(border => border.Tag is MediaEntry) == dockSession.Library.Count,
                "the media desk draws one tile per clip");
            Check(dock.FindName("AudioDeviceBox") is ComboBox { Items.Count: >= 1 },
                "the media desk offers an output speaker picker");
            Check(dock.FindName("ScrubBar") is Slider, "the media desk has a scrub bar");
            host.Close();
        }
        finally { dockSession.Dispose(); }
        Console.WriteLine("PASS: media tags, library round trip, clear, microphones and the dock behave");
    }

    static void GameWindowChecks(List<string> temps)
    {
        MainWindow.PromptOnExit = false;
        var main = new MainWindow(LaunchMode.New) { Width = 1280, Height = 860 };
        main.Show();
        main.UpdateLayout();
        var gameNameBox = (TextBox)main.FindName("GameNameBox")!;
        gameNameBox.Text = "Friday Night";
        // TextChanged is raised after the text is rendered, not at the moment Text is assigned.
        Pump(TimeSpan.FromMilliseconds(50));
        Check(main.Engine.State.GameName == "Friday Night" && gameNameBox.Text == "Friday Night",
            "the top bar names the current game");
        Check(main.RemoteDisplays.Count == main.Scorebug.Displays.Count
              && main.Scorebug.Displays.Any(display => string.Equals(display.Id, main.RemoteDisplayId, StringComparison.OrdinalIgnoreCase)),
            "the phone remote lists every program display and starts on the previewed one");

        var selectorHost = new Window { Width = 240, Height = 120 };
        var selector = new ComboBox
        {
            ItemsSource = new[] { new CompatChoice("football", "Football"), new CompatChoice("basketball", "Basketball") }
        };
        selector.SelectedValuePath = "Id";
        selectorHost.Content = selector;
        selectorHost.Show();
        selectorHost.UpdateLayout();
        selector.SelectedValue = "basketball";
        selectorHost.UpdateLayout();
        Check(selector.SelectedItem is CompatChoice { Id: "basketball" },
            "SelectedValuePath selects the item by its real property");
        selectorHost.Close();

        var displayHost = new Window { Width = 240, Height = 160 };
        var displayList = new ListBox
        {
            ItemsSource = new[] { new CompatChoice("alpha", "Alpha") },
            DisplayMemberPath = "Label"
        };
        displayHost.Content = displayList;
        displayHost.Show();
        displayHost.UpdateLayout();
        Check(All<TextBlock>(displayList).Any(text => text.Text == "Alpha"),
            "DisplayMemberPath renders the chosen property");
        displayHost.Close();

        var scalingProbe = new Image();
        Scoreboard.Desktop.RenderOptions.SetBitmapScalingMode(scalingProbe, BitmapScalingMode.NearestNeighbor);
        Check(Scoreboard.Desktop.RenderOptions.GetBitmapScalingMode(scalingProbe) == BitmapScalingMode.NearestNeighbor
              && Avalonia.Media.RenderOptions.GetBitmapInterpolationMode(scalingProbe) == Avalonia.Media.Imaging.BitmapInterpolationMode.None,
            "bitmap scaling mode reaches Avalonia's interpolation setting");

        // New Game clears both the live game and the on-air copy.
        main.Engine.SetTeams("Old Home", "Old Away");
        main.Engine.SetTeamIdentity(false, "Old Away", "OA", null, "#FFAA0000", "#FF222222");
        main.Scorebug.AddLayout("Old second layout");
        var dock = All<MediaDock>(main).First();
        ((TextBox)dock.FindName("SearchBox")!).Text = "old";
        main.Engine.Score(true, 21);
        main.Engine.AdvanceDown();
        main.Engine.AddStat("Penalties");
        main.PublishOutput();
        main.StartNewGame("football");
        main.UpdateLayout();
        Check(main.Engine.State.HomeScore == 0 && main.Engine.State.Down == 1
              && main.Engine.State.Stats.Select(stat => stat.Name).SequenceEqual(CommonStats.DefaultsFor("football")),
            "New Game resets the previous game to the sport's starting stats");
        Check(main.PublishedState.HomeScore == 0 && main.PublishedState.Down == 1 && main.PublishedState.HomeTeam != "Old Home",
            "New Game leaves nothing of the previous game on air");
        Check(main.Scorebug.Layouts.Count == 1, "New Game starts again with one layout");
        Check(main.Engine.State.GameName.Length == 0 && gameNameBox.Text?.Length == 0,
            "New Game clears the previous game name");
        Check(string.IsNullOrEmpty(((TextBox)dock.FindName("SearchBox")!).Text), "New Game clears the media search box");
        var preview = All<ScorebugCanvasView>(main).FirstOrDefault() ?? throw new Exception("FAIL: the main window has no program preview");
        var previewText = All<TextBlock>(preview).Select(text => text.Text ?? "").ToArray();
        Check(!previewText.Any(text => text.Contains("Old Home", StringComparison.Ordinal) || text.Contains("Old Away", StringComparison.Ordinal) || text == "21"),
            "New Game rebuilds the preview instead of leaving the old artwork");
        main.Close();

        // Saving and reopening restores the text, score and team logo.
        var logo = TempFile(temps, ".png");
        File.WriteAllBytes(logo, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        var game = TempFile(temps, ".spg");
        var saved = new MainWindow(LaunchMode.New);
        saved.Show();
        saved.UpdateLayout();
        saved.Engine.SetTeams("Loaded Home", "Loaded Away");
        saved.Engine.SetTeamIdentity(false, "Loaded Away", "LAW", logo, "#FFCC3355", "#FF223344");
        saved.Engine.SetGameName("Championship Night");
        saved.Engine.Score(true, 7);
        saved.Engine.Save(game);
        saved.Close();

        var reopened = new MainWindow(LaunchMode.Open, game) { Width = 1280, Height = 860 };
        reopened.Show();
        reopened.UpdateLayout();
        Check(reopened.PublishedState.HomeTeam == "Loaded Home" && reopened.PublishedState.HomeScore == 7
              && reopened.PublishedState.AwayTeam == "Loaded Away",
            "reopening a saved game restores its teams and score");
        Check(reopened.Engine.State.GameName == "Championship Night"
              && ((TextBox)reopened.FindName("GameNameBox")!).Text == "Championship Night",
            "a saved game restores its name in the top bar");
        var reopenedPreview = All<ScorebugCanvasView>(reopened).FirstOrDefault()
            ?? throw new Exception("FAIL: the reopened game has no program preview");
        var restoredLogo = All<Image>(reopenedPreview).FirstOrDefault(image => image.Source is not null)
            ?? throw new Exception("FAIL: the saved team logo did not render in the reopened preview");
        Check(reopened.Engine.State.AwayLogoPath is not null && restoredLogo.IsVisible,
            "a saved team logo comes back with the game and renders in the preview");
        reopened.Close();

        // An audience window follows live score and team edits.
        var engine = new GameEngine();
        engine.NewGame("football");
        var catalog = new ScorebugCatalog(TempFile(temps, ".json"));
        var output = new OutputWindow(engine, catalog, "Dark");
        output.Show();
        output.UpdateLayout();
        var home = (TextBlock)output.FindName("HomeLabel")!;
        var renamed = new GameEngine();
        renamed.NewGame("football");
        renamed.SetTeams("Home Squad", "Away Squad");
        output.UpdateLiveScoreClock(renamed.State);
        output.UpdateLayout();
        Check(home.Text == "Home Squad", "renamed teams reach the audience output immediately");
        renamed.SetTeams("ny giants", "Away Squad");
        output.UpdateLiveScoreClock(renamed.State);
        output.UpdateLayout();
        Check(home.Text == "ny giants", "the audience output keeps the case the operator typed");
        Press(output, Key.F11);
        Pump(TimeSpan.FromMilliseconds(100));
        Check(output.IsFullscreen, "F11 makes the audience window fullscreen");
        Press(output, Key.Escape);
        Pump(TimeSpan.FromMilliseconds(100));
        Check(!output.IsFullscreen, "Escape leaves fullscreen");
        output.Close();

        // The launcher offers both ways in and lists recoverable points.
        var demo = new GameEngine();
        demo.NewGame("football");
        demo.SetTeams("Away Club", "Home Club");
        demo.Score(true, 3);
        AutosaveStore.Save(demo);
        var launcher = new LauncherWindow();
        launcher.Show();
        launcher.UpdateLayout();
        Check(ButtonByContent(launcher, "New Game") is not null && ButtonByContent(launcher, "Open Game") is not null,
            "the launcher offers New Game and Open Game");
        Check(All<TextBlock>(launcher).Any(text => text.Text == LauncherWindow.Version),
            "the launcher shows the build version");
        var autosaves = (ListBox)launcher.FindName("AutosaveList")!;
        Check(autosaves.Items.Count > 0, "the launcher lists recoverable autosaves");
        launcher.Close();
        Console.WriteLine("PASS: New Game resets, saved games reopen, output follows live and the launcher behaves");
    }

    static void LayoutChecks(List<string> temps)
    {
        var main = new MainWindow(LaunchMode.New) { Width = 1280, Height = 860 };
        main.Show();
        main.UpdateLayout();
        Check(main.FindName("LayoutPrevButton") is Button && main.FindName("LayoutNextButton") is Button,
            "the program preview has layout arrows");
        main.Scorebug.AddLayout("Slideshow");
        main.Scorebug.Update(layout =>
        {
            layout.Elements.Clear();
            layout.Elements.Add(new ScorebugElement { Name = "Slide", Content = "SLIDE TWO", X = 40, Y = 40, Width = 700, Height = 110, FontSize = 56 });
        });
        main.Scorebug.SelectLayout(0);
        var preview = All<ScorebugCanvasView>(main).First();
        Check(!All<TextBlock>(preview).Any(text => text.Text == "SLIDE TWO"),
            "the preview starts on the first layout");
        main.ShiftLayout(1);
        main.UpdateLayout();
        Check(All<TextBlock>(preview).Any(text => text.Text == "SLIDE TWO"),
            "the forward arrow switches the program preview");
        Check(main.HasStagedChanges, "a layout switch waits for Update Output");

        var audience = new ScorebugCanvasView(main.Scorebug, main.Engine, publishedLayout: true);
        audience.Measure(new Size(1280, 720));
        audience.Arrange(new Rect(0, 0, 1280, 720));
        Check(!All<TextBlock>(audience).Any(text => text.Text == "SLIDE TWO"),
            "an unpublished layout never reaches the audience");
        main.PublishOutput();
        Check(!main.HasStagedChanges, "Update Output publishes the staged layout");
        var onAir = new ScorebugCanvasView(main.Scorebug, main.Engine, publishedLayout: true);
        onAir.Measure(new Size(1280, 720));
        onAir.Arrange(new Rect(0, 0, 1280, 720));
        Check(All<TextBlock>(onAir).Any(text => text.Text == "SLIDE TWO"),
            "Update Output sends the chosen layout to the audience");
        main.ShiftLayout(-1);
        main.UpdateLayout();
        Check(!All<TextBlock>(preview).Any(text => text.Text == "SLIDE TWO"), "the back arrow returns to the first layout");
        var firstDisplayText = All<TextBlock>(preview).Select(text => text.Text ?? "").ToArray();
        Check(firstDisplayText.Length > 0, "the first display's artwork carries text to restore");

        // The display picker repoints the preview; a new display starts blank.
        main.Scorebug.AddDisplay("Lower thirds");
        main.UpdateLayout();
        var displayPicker = (ComboBox)main.FindName("DisplaySelector")!;
        Check(displayPicker.Items.Count == 2, "the display picker lists every display");
        displayPicker.SelectedIndex = 1;
        main.UpdateLayout();
        Check(!All<TextBlock>(preview).Any(), "a new display starts with a blank preview");
        displayPicker.SelectedIndex = 0;
        main.UpdateLayout();
        Check(All<TextBlock>(preview).Select(text => text.Text ?? "").SequenceEqual(firstDisplayText),
            "switching back restores the first display's artwork exactly");

        audience.Dispose();
        onAir.Dispose();
        main.Close();
        Console.WriteLine("PASS: layout arrows, Update Output and the display picker behave");
    }

    static void DisplayChecks(List<string> temps)
    {
        var path = TempFile(temps, ".json");
        var engine = new GameEngine();
        engine.NewGame("football");
        var catalog = new ScorebugCatalog(path);
        var program = catalog.Displays[0];
        var secondIndex = catalog.AddDisplay("Lower thirds");
        var lower = catalog.Displays[secondIndex];
        Check(catalog.Displays.Count == 2 && lower.Layouts.Count == 1 && lower.Layouts[0].Elements.Count == 0,
            "a new display starts with a single blank layout");
        catalog.Update(layout => layout.Elements.Add(new ScorebugElement
        {
            Name = "Strap", Content = "NAME STRAP", X = 100, Y = 500, Width = 800, Height = 120, FontSize = 52
        }));
        catalog.AddLayout("Interview");
        catalog.Update(layout => layout.Elements.Add(new ScorebugElement
        {
            Name = "Interview", Content = "INTERVIEW", X = 60, Y = 60, Width = 700, Height = 110, FontSize = 44
        }));
        var interview = catalog.Layout;
        var interviewIndex = lower.Layouts.FindIndex(layout => layout.Id == interview.Id);
        catalog.SelectDisplay(0);

        // Two windows, one display each: a layout choice never leaks between them.
        var windowA = new ProgramSelection(catalog, program.Id);
        var windowB = new ProgramSelection(catalog, lower.Id);
        windowA.SelectLayout(0);
        Check(windowB.IndexFor(program.Id) == 0 && windowA.LayoutIndex == 0 && windowB.DisplayId == lower.Id,
            "each window keeps its own display and layout choice");

        catalog.SetLink(program.Id, 0, lower.Id, interview.Id);
        Check(catalog.LinkTarget(program.Id, 0) is { } link && link.DisplayId == lower.Id && link.LayoutIndex == interviewIndex,
            "a layout can link to a layout on another display");
        windowA.SelectLayout(1);
        windowA.SelectLayout(0);
        Check(windowA.IndexFor(lower.Id) == interviewIndex,
            "choosing a linked layout switches the other display's selection");
        windowA.PublishStaged();
        Check(catalog.IsPublished(lower.Id, interviewIndex),
            "Update Output sends the linked layout to the second display");

        var output = new OutputWindow(engine, catalog, "Dark", null, lower.Id);
        output.Show();
        output.UpdateLayout();
        Check(output.DisplayId == lower.Id, "an audience window is bound to one display");
        Check(All<TextBlock>(output).Any(text => text.Text == "INTERVIEW"),
            "the display's audience window draws its published layout");
        output.Close();

        // Links can pair both ways, and unticking removes just the second half.
        var studio = new ScorebugEditorWindow(catalog, engine);
        studio.Show();
        studio.UpdateLayout();
        catalog.SelectDisplay(0);
        catalog.SelectLayout(0);
        studio.UpdateLayout();
        var bothWays = (CheckBox)studio.FindName("LinkBothWaysInput")!;
        Check(bothWays.IsChecked != true, "Both ways starts off for a one-way link");
        bothWays.IsChecked = true;
        Check(catalog.LinkTarget(lower.Id, interviewIndex) is { } back && back.DisplayId == program.Id && back.LayoutIndex == 0,
            "Both ways makes the other display link back");
        bothWays.IsChecked = false;
        Check(catalog.LinkTarget(lower.Id, interviewIndex) is null, "unticking Both ways removes only the return link");
        studio.Close();

        var reopened = new ScorebugCatalog(path);
        Check(reopened.Displays.Count == 2 && reopened.Displays[1].Name == "Lower thirds"
              && reopened.LinkTarget(reopened.Displays[0].Id, 0) is not null,
            "displays and layout links persist in the saved design");
        Console.WriteLine("PASS: displays, independent windows and layout links behave");
    }

    /// <summary>
    /// The updater must fetch the asset that matches how this copy was installed: handing a tarball
    /// install the .deb would replace nothing it can use, and an AppImage must never receive the
    /// tarball. Detection lives in AppInfo, selection here, so this pins the pairing.
    /// </summary>
    static void UpdateChannelChecks()
    {
        Check(UpdateFeed.IsAssetFor(InstallKind.Tarball, "ScorePlusLinuxSetup.tar.gz")
              && !UpdateFeed.IsAssetFor(InstallKind.Tarball, "scoreplus_1.5.0_amd64.deb"),
            "a tarball install looks for the stable tarball asset only");
        Check(UpdateFeed.IsAssetFor(InstallKind.AppImage, "ScorePlus-a1.5L-x86_64.AppImage")
              && !UpdateFeed.IsAssetFor(InstallKind.AppImage, "ScorePlusLinuxSetup.tar.gz"),
            "an AppImage install looks for its own AppImage asset only");
        Check(UpdateFeed.IsAssetFor(InstallKind.SystemPackage, "scoreplus_1.5.0_amd64.deb")
              && !UpdateFeed.IsAssetFor(InstallKind.SystemPackage, "ScorePlus-a1.5L-x86_64.AppImage"),
            "a .deb install looks for the package asset only");

        const string feed = """
        [
          {
            "tag_name": "a1.5L",
            "name": "ScorePlus a1.5L (Linux)",
            "draft": false,
            "assets": [
              { "name": "ScorePlus-a1.5L-x86_64.AppImage", "browser_download_url": "https://example.test/appimage", "state": "uploaded" },
              { "name": "scoreplus_1.5.0_amd64.deb", "browser_download_url": "https://example.test/deb", "state": "uploaded" },
              { "name": "ScorePlusLinuxSetup.tar.gz", "browser_download_url": "https://example.test/tarball", "state": "uploaded" }
            ]
          }
        ]
        """;

        Check(UpdateChecker.ParseReleases(feed, InstallKind.Tarball)?.Url == "https://example.test/tarball"
              && UpdateChecker.ParseReleases(feed, InstallKind.AppImage)?.Url == "https://example.test/appimage"
              && UpdateChecker.ParseReleases(feed, InstallKind.SystemPackage)?.Url == "https://example.test/deb",
            "each install kind resolves the release asset built for it");
        Check(UpdateChecker.ParseReleases(feed, InstallKind.Folder) is null
              && UpdateChecker.VersionFromTag("a1.4L") == "1.4.0",
            "a folder build is offered nothing, and a release label maps to a comparable version");

        // Chroma key is applied in the decoder, so the ffmpeg filter carries the layout's colour
        // and tolerance rather than sitting in a Windows-only shader.
        var chroma = new ChromaKeyEffect { KeyColor = Color.FromRgb(0, 177, 64), Tolerance = 0.25 };
        Check(chroma.Filter == "colorkey=0x00B140:0.25:0.1",
            "a chroma key becomes an ffmpeg colorkey filter");

        // The telestrator owns one ink board per display and clears it on dismiss.
        var sharedA = AnnotationBoard.ForDisplay("smoke-display", TimeSpan.FromSeconds(2));
        var sharedB = AnnotationBoard.ForDisplay("smoke-display");
        Check(ReferenceEquals(sharedA, sharedB) && sharedA.Hold == TimeSpan.FromSeconds(2),
            "every window on a display shares one telestrator board");
        var board = new AnnotationBoard(TimeSpan.FromSeconds(1));
        board.Open();
        board.BeginStroke();
        var stroke = new AnnotationStroke { Width = 3 };
        stroke.Points.Add(new Point(1, 1));
        stroke.Points.Add(new Point(5, 5));
        board.Strokes.Add(stroke);
        board.EndStroke();
        Check(board.IsOpen && !board.IsDrawing && board.Strokes.Count == 1,
            "the telestrator records a finished stroke");
        board.Dismiss();
        Check(!board.IsOpen && board.Strokes.Count == 0,
            "dismissing the telestrator clears the display's ink");
    }

    // ---------------------------------------------------------------- helpers

    sealed class HeadlessApp : App
    {
        public override void OnFrameworkInitializationCompleted() { }
    }

    sealed record CompatChoice(string Id, string Label);

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static string TempFile(List<string> temps, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"smoke-{Guid.NewGuid():N}{extension}");
        temps.Add(path);
        return path;
    }

    static void Pump(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(10);
        } while (DateTime.UtcNow < deadline);
    }

    static IEnumerable<T> All<T>(Visual root) where T : Visual => root.GetVisualDescendants().OfType<T>();

    static Button? ButtonByContent(Visual root, string text)
        => All<Button>(root).FirstOrDefault(button => (button.Content as string) == text);

    static void Click(Button? button)
    {
        if (button is null) throw new Exception("FAIL: a button the check needs is missing");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    static void Press(InputElement target, Key key)
        => target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            PhysicalKey = PhysicalKey.None,
            KeyModifiers = KeyModifiers.None
        });
}
