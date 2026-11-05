using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.IO;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public partial class App : Application
{
    // The console is a classic desktop app: one lifetime owns the main window and every
    // popup that shows against it, and Start() blocks until the lifetime shuts down.
    static void Main()
    {
        desktop = new ClassicDesktopStyleApplicationLifetime();
        desktop.Start();
    }

    private static ClassicDesktopStyleApplicationLifetime desktop;

    public async override void OnFrameworkInitializationCompleted()
    {
        // Media decoders and other native components can fail on threads the dispatcher never
        // sees. Log those too, so a window that closes on its own leaves a trace to diagnose.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // The main window opens first and stays locked behind the launch box, so the operator can
        // see the workspace they are about to load into before choosing a game.
        MainWindow window;
        try { window = new MainWindow(waitForLaunch: true); }
        catch (Exception ex)
        {
            LogCrash(ex);
            NoticeWindow.Tell(null, "ScorePlus could not start", ex.Message, warning: true);
            desktop.TryShutdown();
            return;
        }
        desktop.MainWindow = window;
        // Avalonia has no per-window Closed hook; Window fires Activated when the main
        // window closes, which is what shuts the app down once it is gone.
        window.Activated += (_, _) => desktop.TryShutdown();

        // The console opens filling the screen — full windowed, so the title bar and taskbar stay —
        // and ScreenFit keeps that true on a portrait monitor as well as a wide one.
        Application.Current.ApplicationLifetime = desktop;
        window.WindowState = WindowState.Maximized;
        window.Show();

        var launcher = new LauncherWindow();
        launcher.Show(window);
        if (await launcher.ShowDialog<bool>(window) != true) { window.Close(); return; }
        window.StartFromLauncher(launcher.Mode, launcher.GamePath, launcher.SportId);
    }

    /// <summary>Appends a timestamped error to <c>logs/errors.log</c> under the app's config root.</summary>
    static void LogCrash(Exception? error)
    {
        try
        {
            var folder = AppPaths.Combine("logs");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:u} {error}\n\n");
        }
        catch
        {
            // Logging must never be the reason the app fails to report a problem.
        }
    }
}
