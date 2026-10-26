using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Scoreboard.Desktop;

public sealed class UserPreferences
{
    public string Theme { get; set; } = "Dark";
    public bool SeparateControls { get; set; }

    public UserPreferences Clone() => new() { Theme = Theme, SeparateControls = SeparateControls };
}

public static class PreferencesStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Scoreboard", "preferences.json");

    public static UserPreferences Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { return new(); }
    }

    public static void Save(UserPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}

/// <summary>
/// Applies operator chrome colors (panels, inputs, buttons) for the selected theme.
/// Graphics that carry their own designed colors — the scorebug canvas and audience output —
/// are deliberately skipped so a theme switch never recolors on-air artwork.
/// </summary>
public static class ThemeManager
{
    public static readonly DependencyProperty SkipThemeProperty =
        DependencyProperty.RegisterAttached("SkipTheme", typeof(bool), typeof(ThemeManager), new PropertyMetadata(false));

    public static void SetSkipTheme(DependencyObject element, bool value) => element.SetValue(SkipThemeProperty, value);
    public static bool GetSkipTheme(DependencyObject element) => (bool)element.GetValue(SkipThemeProperty);

    public static Color PanelColor(string theme) => theme == "Light" ? Color.FromRgb(255, 255, 255) : Color.FromRgb(23, 28, 37);

    public static void Apply(Window window, string theme)
    {
        var light = theme == "Light";
        var background = light ? "#FFF3F5F8" : "#FF000000";
        var panel = light ? "#FFFFFFFF" : "#FF080808";
        var surface = light ? "#FFE5EAF0" : "#FF141414";
        var foreground = light ? "#FF18202B" : "#FFF4F7FB";
        var muted = light ? "#FF586576" : "#FFB0B0B0";
        var input = light ? "#FFFFFFFF" : "#FF050505";
        var border = light ? "#FFB9C3CE" : "#FF383838";

        window.Background = Brush(background);
        window.Foreground = Brush(foreground);

        void ApplyTheme() => SetDescendants(window.Content as DependencyObject, panel, surface, foreground, input, border);
        window.Loaded += (_, _) => ApplyTheme();
        if (window.IsLoaded) window.Dispatcher.BeginInvoke(ApplyTheme);
    }

    static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    static void SetDescendants(DependencyObject? root, string panel, string surface, string foreground, string input, string border)
    {
        if (root is null) return;
        // Never repaint designed artwork: scorebug views and the studio canvas keep their own colors.
        if (root is ScorebugCanvasView) return;
        if (root is FrameworkElement { Name: "DesignCanvas" }) return;
        if (GetSkipTheme(root)) return;

        if (root is Border borderElement && borderElement.Background is SolidColorBrush) borderElement.Background = Brush(panel);
        if (root is Panel panelElement && panelElement.Background is SolidColorBrush) panelElement.Background = Brush(panel);
        if (root is TextBlock text && (text.Foreground is not SolidColorBrush solid || !PreserveTextColor(solid)))
            text.Foreground = Brush(foreground);
        if (root is ComboBoxItem comboItem)
        {
            comboItem.Foreground = Brush(foreground);
            comboItem.Background = Brush(surface);
        }
        if (root is Control control)
        {
            control.Foreground = Brush(foreground);
            switch (control)
            {
                case TextBox: control.Background = Brush(input); control.BorderBrush = Brush(border); break;
                case Button: control.Background = Brush(surface); control.BorderBrush = Brush(border); break;
                case ListBox: control.Background = Brush(input); control.BorderBrush = Brush(border); break;
                case ComboBox: control.Background = Brush(input); control.BorderBrush = Brush(border); break;
                case ToggleButton: control.Background = Brush(surface); control.BorderBrush = Brush(border); break;
            }
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            SetDescendants(VisualTreeHelper.GetChild(root, i), panel, surface, foreground, input, border);
    }

    static bool PreserveTextColor(SolidColorBrush brush) =>
        brush.Color == Color.FromRgb(56, 215, 163) || brush.Color == Color.FromRgb(255, 209, 102) || brush.Color == Color.FromRgb(255, 107, 107);
}
