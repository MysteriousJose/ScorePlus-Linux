namespace Scoreboard.Core;

public static class PeriodDisplay
{
    /// <summary>The three ways a quarter can be printed, as offered in the studio.</summary>
    public static readonly (string Key, string Label)[] Formats =
    [
        ("Full", "Quarter 1"),
        ("Short", "Q1"),
        ("Number", "1")
    ];

    public static string Format(GameState state) => Format(state, null);

    /// <param name="formatOverride">A studio element's own format ("Full", "Short", "Number"),
    /// or null to use the game-wide choice in <see cref="GameState.QuarterDisplayFormat"/>.</param>
    public static string Format(GameState state, string? formatOverride = null)
    {
        var format = formatOverride is "Full" or "Short" or "Number" ? formatOverride : state.QuarterDisplayFormat;
        if (!state.PeriodLabel.Equals("Quarter", StringComparison.OrdinalIgnoreCase)) format = "Full";
        var label = format switch
        {
            "Short" => state.PeriodLabel.Equals("Quarter", StringComparison.OrdinalIgnoreCase) ? "Q" : state.PeriodLabel,
            "Number" => "",
            _ => state.PeriodLabel
        };
        if (label.Length == 0) return state.Period.ToString();
        // "Q1" reads as one chip; "Quarter 1" and "Half 1" keep their space.
        return label.Length == 1 ? $"{label}{state.Period}" : $"{label} {state.Period}";
    }
}
