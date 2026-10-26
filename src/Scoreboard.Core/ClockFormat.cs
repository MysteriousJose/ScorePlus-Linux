namespace Scoreboard.Core;

public static class ClockFormat
{
    /// <summary>Formats seconds as MM:SS with minutes unbounded.</summary>
    public static string Format(int seconds)
    {
        var clamped = Math.Max(0, seconds);
        return $"{clamped / 60:00}:{clamped % 60:00}";
    }

    /// <summary>Parses "MM:SS", "M:SS", or a whole number of seconds.</summary>
    public static bool TryParse(string text, out int seconds)
    {
        seconds = 0;
        var value = (text ?? "").Trim();
        if (value.Length == 0) return false;
        if (value.Contains(':'))
        {
            var parts = value.Split(':');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out var minutes) || !int.TryParse(parts[1], out var secs)) return false;
            if (minutes < 0 || secs is < 0 or > 59) return false;
            seconds = minutes * 60 + secs;
            return true;
        }
        if (!int.TryParse(value, out var total) || total < 0) return false;
        seconds = total;
        return true;
    }
}
