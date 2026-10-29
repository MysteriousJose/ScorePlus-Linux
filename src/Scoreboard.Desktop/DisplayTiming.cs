using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// How long a graphic waits before printing a changed value. It belongs to the show, not to one
/// element, so every preview and audience window follows the same choice — set from Preferences.
/// </summary>
public static class DisplayTiming
{
    /// <summary>Seconds a value is held before it switches. 0 switches instantly.</summary>
    public static double ValueSwitchDelay { get; set; }

    /// <summary>Reads the delay from the operator's preferences.</summary>
    public static void Apply(UserPreferences preferences) =>
        ValueSwitchDelay = Math.Clamp(preferences.ValueSwitchDelay, 0, 10);
}
