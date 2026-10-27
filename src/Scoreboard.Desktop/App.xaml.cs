using System.Windows;
using System.IO;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Media decoders and other native components can fail on threads the dispatcher never
        // sees. Log those too, so a window that closes on its own leaves a trace to diagnose.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            MessageBox.Show("ScorePlus hit an unexpected error and kept your game autosave.\n\n" + args.Exception.Message,
                "ScorePlus", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);

        // The launcher keeps the process alive until it decides which window to open.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var launcher = new LauncherWindow();
        if (launcher.ShowDialog() != true) { Shutdown(); return; }

        MainWindow window;
        try { window = new MainWindow(launcher.Mode, launcher.GamePath); }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show($"ScorePlus could not start: {ex.Message}", "ScorePlus", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    /// <summary>Appends a timestamped error to <c>%LOCALAPPDATA%\ScorePlus\logs\errors.log</c>.</summary>
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
