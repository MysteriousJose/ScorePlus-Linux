using System.Windows;
using Microsoft.Win32;

namespace Scoreboard.Desktop;

public enum LaunchMode { New, Open }

/// <summary>
/// The first thing that opens: pick a new game or an existing one. It holds no game state itself;
/// the main window is created afterwards with the choice below.
/// </summary>
public partial class LauncherWindow : Window
{
    /// <summary>The product version shown on the side strip.</summary>
    public const string Version = "v0.0";

    public LaunchMode Mode { get; private set; } = LaunchMode.New;

    public string? GamePath { get; private set; }

    public LauncherWindow()
    {
        InitializeComponent();
        VersionText.Text = Version;
        ThemeManager.SetSkipTheme(this, true);
        Loaded += (_, _) => NewGameButton.Focus();
    }

    void NewGame_Click(object sender, RoutedEventArgs e)
    {
        Mode = LaunchMode.New;
        GamePath = null;
        DialogResult = true;
        Close();
    }

    void OpenGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open game",
            Filter = "Scoreboard Game (*.scoreboard;*.json)|*.scoreboard;*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        Mode = LaunchMode.Open;
        GamePath = dialog.FileName;
        DialogResult = true;
        Close();
    }
}
