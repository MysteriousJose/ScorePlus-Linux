using System.Windows;
using System.IO;
namespace Scoreboard.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try { var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scoreboard", "logs"); Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:u} {args.Exception}\n"); } catch { }
            MessageBox.Show("The application encountered an error. Your game autosave is preserved.\n\n" + args.Exception.Message, "Scoreboard", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}
