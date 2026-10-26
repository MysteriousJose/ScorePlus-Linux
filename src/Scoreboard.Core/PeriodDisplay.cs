namespace Scoreboard.Core;

public static class PeriodDisplay
{
    public static string Format(GameState state)
    {
        var format = state.PeriodLabel.Equals("Quarter", StringComparison.OrdinalIgnoreCase)
            ? state.QuarterDisplayFormat
            : "Full";
        var label = format switch
        {
            "Short" => state.PeriodLabel.Equals("Quarter", StringComparison.OrdinalIgnoreCase) ? "Q" : state.PeriodLabel,
            "Number" => "",
            _ => state.PeriodLabel
        };
        return label.Length == 0 ? state.Period.ToString() : $"{label} {state.Period}";
    }
}
