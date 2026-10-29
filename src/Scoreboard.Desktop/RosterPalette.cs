using System.Windows;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// The roster desk's own colour set. It is deliberately charcoal (not the navy of the older
/// panels), so the desk reads like a player database: a dark page, slightly lighter columns,
/// dark input wells and one green header. Both themes are painted from here, and the panels pick
/// the brushes up through DynamicResource so rows built later are themed too.
/// </summary>
internal static class RosterPalette
{
    public static void Apply(string theme, ResourceDictionary resources)
    {
        var light = ThemeManager.Resolve(theme) == "Light";
        Set(resources, "RosterPanelBrush", light ? "#E9EDF2" : "#141414");
        Set(resources, "RosterSurfaceBrush", light ? "#FFFFFF" : "#242424");
        Set(resources, "RosterSurfaceAltBrush", light ? "#E2E7ED" : "#2E2E2E");
        Set(resources, "RosterInputBrush", light ? "#FFFFFF" : "#191919");
        Set(resources, "RosterBorderBrush", light ? "#C3CBD5" : "#3C3C3C");
        Set(resources, "RosterTextBrush", light ? "#18202B" : "#F2F2F2");
        Set(resources, "RosterMutedBrush", light ? "#5C6B7E" : "#9BA1A8");
        Set(resources, "RosterHeaderBrush", light ? "#17A673" : "#38D7A3");
        Set(resources, "RosterHeaderTextBrush", light ? "#FFFFFF" : "#062018");
        Set(resources, "RosterAccentBrush", light ? "#12855C" : "#38D7A3");
        Set(resources, "RosterAccentTextBrush", light ? "#FFFFFF" : "#062018");
        Set(resources, "RosterDangerBrush", light ? "#C0392B" : "#FF6B6B");
    }

    static void Set(ResourceDictionary resources, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        resources[key] = brush;
    }

    /// <summary>
    /// The highlight for the row that is on the graphic: the team's primary colour, so the desk
    /// belongs to that team. Returns the background and the text colour that reads on it.
    /// </summary>
    public static (Brush Background, Brush Foreground) Highlight(string? teamColor, ResourceDictionary resources)
    {
        var fallback = resources["RosterAccentBrush"] as Brush ?? Brushes.LimeGreen;
        Brush background = fallback;
        try
        {
            if (!string.IsNullOrWhiteSpace(teamColor))
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(teamColor));
                brush.Freeze();
                background = brush;
            }
        }
        catch { }
        var color = background is SolidColorBrush solid ? solid.Color : Colors.LimeGreen;
        // Readability first: light team colours get dark text.
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return luminance > 0.6
            ? (background, new SolidColorBrush(Color.FromRgb(12, 24, 20)))
            : (background, Brushes.White);
    }
}
