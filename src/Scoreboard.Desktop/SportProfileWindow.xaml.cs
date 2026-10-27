using System.Windows;
using System.Windows.Controls;
using Scoreboard.Core;

namespace Scoreboard.Desktop;
public partial class SportProfileWindow : Window
{
    readonly SportCatalog _catalog;
    SportDefinition? _selected;
    bool _loading;

    /// <summary>True once this session saved, duplicated, reset or deleted a profile, so the app
    /// knows to refresh the sport picker and the console when the window closes.</summary>
    public bool Changed { get; private set; }

    public SportProfileWindow(SportCatalog catalog)
    {
        InitializeComponent();
        _catalog = catalog;
        RefreshList();
        Profiles.SelectedIndex = 0;
    }

    void RefreshList(string? selectId = null)
    {
        _loading = true;
        Profiles.ItemsSource = null;
        Profiles.ItemsSource = _catalog.All.Select(s => new ProfileItem(s, Label(s))).ToArray();
        _loading = false;
        if (selectId is not null)
            Profiles.SelectedItem = Profiles.Items.Cast<ProfileItem>().FirstOrDefault(i => i.Profile.Id == selectId);
        else if (Profiles.SelectedIndex < 0 && Profiles.Items.Count > 0)
            Profiles.SelectedIndex = 0;
    }

    string Label(SportDefinition sport)
    {
        var kind = _catalog.IsOverridden(sport.Id) ? "EDITED" : _catalog.IsBuiltIn(sport.Id) ? "PRESET" : "CUSTOM";
        return $"{kind}   ·   {sport.Name}";
    }

    void Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Profiles.SelectedItem is not ProfileItem item) return;
        _selected = item.Profile;
        _loading = true;
        NameInput.Text = _selected.Name;
        UnitInput.Text = _selected.Unit;
        PeriodsInput.Text = _selected.Periods.ToString();
        MinutesInput.Text = (_selected.PeriodSeconds / 60).ToString();
        TimeoutsInput.Text = _selected.Timeouts.ToString();
        CountUpInput.IsChecked = _selected.CountUp;
        ScoringInput.Text = string.Join(", ", _selected.ScoringOptions);
        _loading = false;
        ErrorText.Text = "";
        var builtIn = _catalog.IsBuiltIn(_selected.Id);
        BuiltInNote.Text = builtIn
            ? "Built-in preset — saving keeps your version as an override, so new games use these settings."
            : "Custom profile — saving updates it everywhere.";
        ResetButton.IsEnabled = _catalog.IsOverridden(_selected.Id);
        DeleteButton.IsEnabled = !builtIn;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try
        {
            var profile = ReadInputs(_selected);
            var saved = _catalog.SaveProfile(profile);
            _selected = saved;
            Changed = true;
            RefreshList(saved.Id);
            ErrorText.Text = "Saved. New games use these settings.";
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or InvalidOperationException)
        {
            ErrorText.Text = ex.Message;
        }
    }

    SportDefinition ReadInputs(SportDefinition source)
    {
        var scores = ScoringInput.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        if (scores.Length == 0) throw new FormatException("Enter at least one scoring value, for example 1, 2, 3.");
        return source with
        {
            Name = NameInput.Text.Trim(),
            Unit = UnitInput.Text.Trim(),
            Periods = int.Parse(PeriodsInput.Text),
            PeriodSeconds = checked(int.Parse(MinutesInput.Text) * 60),
            Timeouts = int.Parse(TimeoutsInput.Text),
            CountUp = CountUpInput.IsChecked == true,
            ScoringOptions = scores
        };
    }

    void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try
        {
            var name = NameInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || _catalog.All.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) && s.Id != _selected.Id))
                name = "Copy of " + _selected.Name;
            _catalog.Duplicate(_selected.Id, name);
            Changed = true;
            var created = _catalog.Custom.Last();
            RefreshList(created.Id);
            Profiles.SelectedItem = Profiles.Items.Cast<ProfileItem>().FirstOrDefault(i => i.Profile.Id == created.Id);
            ErrorText.Text = "Created a custom copy.";
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !_catalog.IsOverridden(_selected.Id)) return;
        if (MessageBox.Show($"Discard your saved changes to ‘{_selected.Name}’ and restore the original preset?", "Reset to preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _catalog.ResetOverride(_selected.Id);
        Changed = true;
        RefreshList(_selected.Id);
        Profiles.SelectedItem = Profiles.Items.Cast<ProfileItem>().FirstOrDefault(i => i.Profile.Id == _selected?.Id);
        ErrorText.Text = "Restored the original preset.";
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _catalog.IsBuiltIn(_selected.Id)) return;
        if (MessageBox.Show($"Delete the custom sport ‘{_selected.Name}’? Existing games keep their saved settings.", "Delete custom sport", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _catalog.Delete(_selected.Id);
        Changed = true;
        _selected = null;
        RefreshList();
    }

    void Done_Click(object sender, RoutedEventArgs e) => Close();

    sealed record ProfileItem(SportDefinition Profile, string Text) { public override string ToString() => Text; }
}
