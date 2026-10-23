using System.Windows;
using System.Windows.Controls;
using Scoreboard.Core;

namespace Scoreboard.Desktop;
public partial class SportProfileWindow : Window
{
    readonly SportCatalog _catalog;
    SportDefinition? _selected;
    public SportProfileWindow(SportCatalog catalog)
    {
        InitializeComponent(); _catalog = catalog; RefreshList(); Profiles.SelectedIndex = 0;
    }
    void RefreshList(string? selectId = null)
    {
        Profiles.ItemsSource = null; Profiles.ItemsSource = _catalog.All.Select(s => new ProfileItem(s, _catalog.Custom.Any(c => c.Id == s.Id) ? "CUSTOM   ·   " + s.Name : "PRESET   ·   " + s.Name)).ToArray();
        if (selectId is not null) Profiles.SelectedItem = Profiles.Items.Cast<ProfileItem>().FirstOrDefault(i => i.Profile.Id == selectId);
    }
    void Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Profiles.SelectedItem is not ProfileItem item) return;
        _selected = item.Profile;
        NameInput.Text = _selected.Name; UnitInput.Text = _selected.Unit; PeriodsInput.Text = _selected.Periods.ToString();
        MinutesInput.Text = (_selected.PeriodSeconds / 60).ToString(); CountUpInput.IsChecked = _selected.CountUp;
        ScoringInput.Text = string.Join(", ", _selected.ScoringOptions); ErrorText.Text = "";
        var custom = _catalog.Custom.Any(c => c.Id == _selected.Id);
        NameInput.IsReadOnly = !custom; UnitInput.IsReadOnly = !custom; PeriodsInput.IsReadOnly = !custom;
        MinutesInput.IsReadOnly = !custom; CountUpInput.IsEnabled = custom; ScoringInput.IsReadOnly = !custom;
        BuiltInNote.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = custom; DeleteButton.IsEnabled = custom; DuplicateButton.IsEnabled = true;
    }
    void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try
        {
            var name = NameInput.Text.Trim(); if (string.IsNullOrWhiteSpace(name) || _catalog.All.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) name = "Custom " + _selected.Name;
            _catalog.Duplicate(_selected.Id, name); var created = _catalog.Custom.Last(); RefreshList(created.Id); ErrorText.Text = "Custom sport created.";
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try
        {
            var scores = ScoringInput.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            var profile = _selected with { Name = NameInput.Text.Trim(), Unit = UnitInput.Text.Trim(), Periods = int.Parse(PeriodsInput.Text), PeriodSeconds = checked(int.Parse(MinutesInput.Text) * 60), CountUp = CountUpInput.IsChecked == true, ScoringOptions = scores };
            _catalog.AddOrUpdate(profile); RefreshList(profile.Id); ErrorText.Text = "Saved. New games will use these settings.";
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException) { ErrorText.Text = ex.Message; }
    }
    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !_catalog.Custom.Any(s => s.Id == _selected.Id)) return;
        if (MessageBox.Show($"Delete the custom sport ‘{_selected.Name}’? Existing games will keep their saved settings.", "Delete custom sport", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _catalog.Delete(_selected.Id); RefreshList(); Profiles.SelectedIndex = 0;
    }
    void Done_Click(object sender, RoutedEventArgs e) => Close();
    sealed record ProfileItem(SportDefinition Profile, string Label) { public override string ToString() => Label; }
}
