using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Scoreboard.Core;
namespace Scoreboard.Desktop;
public partial class SettingsWindow : Window
{
    /// <summary>The longest switch delay the graphic accepts, in seconds.</summary>
    const double MaxDelay = 5;

    readonly UserPreferences _original;
    readonly UserPreferences _draft;
    bool _loading = true;
    bool _deleteArmed;

    public SettingsWindow(UserPreferences prefs)
    {
        InitializeComponent();
        _original = prefs;
        _draft = prefs.Clone();
        ThemeSelector.ItemsSource = new[] { "System", "Dark", "Light" };
        ThemeSelector.SelectedItem = ThemeSelector.Items.Contains(_draft.Theme) ? _draft.Theme : "System";
        ShowHints.IsChecked = _draft.ShowHeadingHints;
        DelayInput.Text = _draft.ValueSwitchDelay.ToString("0.##", CultureInfo.CurrentCulture);
        _loading = false;
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeSelector.SelectedItem is string theme) _draft.Theme = theme;
    }

    void Controls_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || ShowHints is null) return;
        _draft.ShowHeadingHints = ShowHints.IsChecked == true;
    }

    /// <summary>
    /// Any value the operator types is used as-is inside 0–5, so 0.35 s is a real answer rather
    /// than the nearest item in a fixed list. Half-typed text ("", "0.") leaves the draft alone.
    /// </summary>
    void Delay_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || DelayInput is null) return;
        if (!double.TryParse(DelayInput.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds)) return;
        _draft.ValueSwitchDelay = Math.Clamp(seconds, 0, MaxDelay);
    }

    /// <summary>Clears every recovery point; a second click confirms, so one tap cannot wipe them.</summary>
    void DeleteAutosaves_Click(object sender, RoutedEventArgs e)
    {
        var remaining = AutosaveStore.List().Count;
        if (remaining == 0)
        {
            AutosaveStatus.Text = "There are no autosaves to delete.";
            DeleteAutosavesButton.Content = "Delete all autosaves";
            _deleteArmed = false;
            return;
        }
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            DeleteAutosavesButton.Content = $"Delete {remaining} autosave{(remaining == 1 ? "" : "s")}?";
            AutosaveStatus.Text = "Click again to remove every autosave on this computer.";
            return;
        }
        var deleted = AutosaveStore.DeleteAll();
        _deleteArmed = false;
        DeleteAutosavesButton.Content = "Delete all autosaves";
        AutosaveStatus.Text = deleted == 0
            ? "No autosaves could be deleted."
            : $"Deleted {deleted} autosave{(deleted == 1 ? "" : "s")}. Saved .spg games are untouched.";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _original.Theme = _draft.Theme;
        _original.ShowHeadingHints = _draft.ShowHeadingHints;
        _original.ValueSwitchDelay = Math.Clamp(_draft.ValueSwitchDelay, 0, MaxDelay);
        PreferencesStore.Save(_original);
        DisplayTiming.Apply(_original);
        // DialogResult is only valid for a modal dialog; the window may also be shown modelessly.
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true;
        Close();
    }

    // Closing with Cancel returns null, so MainWindow leaves the live theme untouched.
    void Done_Click(object sender, RoutedEventArgs e) => Close();
}
