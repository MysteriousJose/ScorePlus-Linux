using System.IO;
using System.Text.Json;
using Scoreboard.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Scoreboard.Desktop;

public sealed class UserPreferences
{
    /// <summary>Bumped when the shipped defaults change, so older files take the new defaults once.</summary>
    public int Schema { get; set; }

    public string Theme { get; set; } = "Dark";
    public bool ShowHeadingHints { get; set; }

    /// <summary>
    /// How long the graphic holds an old value before printing the new one, in seconds. 0 is
    /// instant. This is a show-wide timing choice, not a property of one element.
    /// </summary>
    public double ValueSwitchDelay { get; set; }

    /// <summary>The sport the operator last ran, so a launcher "New Game" opens the game they expect.</summary>
    public string LastSport { get; set; } = "football";

    public List<MediaEntry> Media { get; set; } = [];

    /// <summary>Saved media playlists: an ordered run of clips that can repeat.</summary>
    public List<MediaPlaylist> Playlists { get; set; } = [];

    /// <summary>Folders created before they hold a clip, so an empty folder stays on the shelf.</summary>
    public List<string> MediaFolders { get; set; } = [];

    /// <summary>Older preference files stored plain paths; they are migrated into <see cref="Media"/> on load.</summary>
    public List<string> MediaFiles { get; set; } = [];

    public UserPreferences Clone() => new()
    {
        Schema = Schema,
        Theme = Theme,
        ShowHeadingHints = ShowHeadingHints,
        ValueSwitchDelay = ValueSwitchDelay,
        LastSport = LastSport,
        Media = [.. Media],
        Playlists = Playlists.Select(p => new MediaPlaylist { Name = p.Name, Loop = p.Loop, Paths = p.Paths.ToList() }).ToList(),
        MediaFolders = [.. MediaFolders],
        MediaFiles = [.. MediaFiles]
    };
}

public static class PreferencesStore
{
    static string? _overridePath;

    /// <summary>Test hook: aim storage at a throwaway file so a suite can never rewrite the operator's real preferences.</summary>
    public static void UsePath(string? path) => _overridePath = path;

    static string FilePath => _overridePath ?? AppPaths.Combine("preferences.json");

    public static UserPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var loaded = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(FilePath)) ?? new();
            Migrate(loaded);
            return loaded;
        }
        catch { return new(); }
    }

    static void Migrate(UserPreferences preferences)
    {
        preferences.Media ??= [];
        preferences.Playlists ??= [];
        preferences.MediaFolders ??= [];
        preferences.MediaFiles ??= [];
        if (preferences.Media.Count == 0 && preferences.MediaFiles.Count > 0)
            preferences.Media = preferences.MediaFiles.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => new MediaEntry(p)).ToList();
        preferences.MediaFiles.Clear();

        // Files written before the schema existed carried the old light default. Take the shipped
        // defaults once (black theme, detached panel, no heading tips); later choices are respected.
        if (preferences.Schema < 1)
        {
            preferences.Theme = "Dark";
            preferences.ShowHeadingHints = false;
            preferences.Schema = 1;
            try { Save(preferences); } catch { }
        }
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
        // The shared control styles read these. Setting them on the window means a button built
        // later — a stepper, a rebuilt sport row, a dialog's extra action — still matches the theme
        // instead of falling back to the startup shades.
        window.Resources["OperatorPanelBrush"] = Brush(panel);
        window.Resources["OperatorSurfaceBrush"] = Brush(surface);
        window.Resources["OperatorInputBrush"] = Brush(input);
        window.Resources["OperatorTextBrush"] = Brush(foreground);
        window.Resources["OperatorBorderBrush"] = Brush(border);

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
