using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Asks which display an audience window should show. Only used when a game actually has more than
/// one display; with a single display the answer is obvious and no window is shown.
/// </summary>
internal sealed class DisplayPickerWindow : Window
{
    readonly ListBox _list = new();
    readonly ScorebugCatalog _catalog;
    string? _result;

    DisplayPickerWindow(ScorebugCatalog catalog, string theme, string? preferredDisplayId)
    {
        _catalog = catalog;
        Title = "Audience output";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(new TextBlock
        {
            Text = "Which display is this window for?",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Each display has its own audience window and its own layout. Opening one again adds another window rather than replacing the first.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _list.ItemsSource = catalog.Displays.Select(display => new DisplayChoice(display)).ToArray();
        _list.DisplayMemberPath = "Label";
        _list.FontSize = 13;
        _list.MinHeight = 96;
        _list.Margin = new Thickness(0, 0, 0, 12);
        var preferred = catalog.Displays.FindIndex(display => string.Equals(display.Id, preferredDisplayId, StringComparison.OrdinalIgnoreCase));
        _list.SelectedIndex = preferred >= 0 ? preferred : 0;
        _list.MouseDoubleClick += (_, _) => Accept();
        root.Children.Add(_list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        var open = new Button { Content = "Open output", IsDefault = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        open.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(open);
        root.Children.Add(buttons);
        Content = root;
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) => _list.Focus();
    }

    void Accept()
    {
        if (_list.SelectedItem is not DisplayChoice choice) return;
        _result = choice.Display.Id;
        DialogResult = true;
        Close();
    }

    /// <summary>The chosen display id, or null when the operator cancelled.</summary>
    public static string? Ask(Window owner, ScorebugCatalog catalog, string theme, string? preferredDisplayId)
    {
        var window = new DisplayPickerWindow(catalog, theme, preferredDisplayId) { Owner = owner };
        return window.ShowDialog() == true ? window._result : null;
    }

    sealed class DisplayChoice
    {
        public DisplayChoice(ScorebugDisplay display)
        {
            Display = display;
            var live = display.Layouts.Count == 0 ? "no layouts" : display.Layouts[Math.Clamp(display.PublishedIndex, 0, display.Layouts.Count - 1)].Name;
            Label = $"{display.Name}   ·   on air: {live}   ·   {display.Layouts.Count} layout{(display.Layouts.Count == 1 ? "" : "s")}";
        }

        public ScorebugDisplay Display { get; }
        public string Label { get; }
    }
}
