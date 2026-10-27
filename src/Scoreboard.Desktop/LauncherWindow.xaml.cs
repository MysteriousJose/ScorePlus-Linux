using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public enum LaunchMode { New, Open }

/// <summary>
/// The first thing that opens: pick a new game, an existing one, or an autosave. It holds no game
/// state itself; the main window is created afterwards with the choice below.
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
        RefreshAutosaves();
        Loaded += (_, _) => NewGameButton.Focus();
    }

    /// <summary>The recoverable points offered in the list, newest first.</summary>
    public IReadOnlyList<AutosaveEntry> Autosaves { get; private set; } = [];

    void RefreshAutosaves()
    {
        AutosaveStore.ImportLegacy();
        Autosaves = AutosaveStore.List();
        AutosaveList.ItemsSource = Autosaves;
        AutosaveEmpty.Visibility = Autosaves.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AutosaveList.Visibility = Autosaves.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        LoadAutosaveButton.IsEnabled = false;
        if (Autosaves.Count > 0) AutosaveList.SelectedIndex = 0;
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
            DefaultExt = "spg",
            Filter = "ScorePlus Game (*.spg)|*.spg|Scoreboard Game (*.scoreboard;*.json)|*.scoreboard;*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        Finish(LaunchMode.Open, dialog.FileName);
    }

    void AutosaveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => LoadAutosaveButton.IsEnabled = AutosaveList.SelectedItem is AutosaveEntry;

    void AutosaveList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => LoadSelectedAutosave();

    void LoadAutosave_Click(object sender, RoutedEventArgs e) => LoadSelectedAutosave();

    void LoadSelectedAutosave()
    {
        if (AutosaveList.SelectedItem is not AutosaveEntry entry) return;
        if (!File.Exists(entry.Path))
        {
            RefreshAutosaves();
            return;
        }
        Finish(LaunchMode.Open, entry.Path);
    }

    void Finish(LaunchMode mode, string path)
    {
        Mode = mode;
        GamePath = path;
        DialogResult = true;
        Close();
    }
}
