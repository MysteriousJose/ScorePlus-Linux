using System.Windows;
using System.Windows.Controls;
namespace Scoreboard.Desktop;
public partial class SettingsWindow : Window
{
    readonly UserPreferences _original;
    readonly UserPreferences _draft;
    bool _loading = true;

    public SettingsWindow(UserPreferences prefs)
    {
        InitializeComponent();
        _original = prefs;
        _draft = prefs.Clone();
        ThemeSelector.ItemsSource = new[] { "Dark", "Light" };
        ThemeSelector.SelectedItem = _draft.Theme;
        SeparateControls.IsChecked = _draft.SeparateControls;
        _loading = false;
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeSelector.SelectedItem is string theme) _draft.Theme = theme;
    }

    void Controls_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && SeparateControls is not null) _draft.SeparateControls = SeparateControls.IsChecked == true;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _original.Theme = _draft.Theme;
        _original.SeparateControls = _draft.SeparateControls;
        PreferencesStore.Save(_original);
        // DialogResult is only valid for a modal dialog; the window may also be shown modelessly.
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true;
        Close();
    }

    // Closing with Cancel returns null, so MainWindow leaves the live theme untouched.
    void Done_Click(object sender, RoutedEventArgs e) => Close();
}
