using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace Scoreboard.Desktop;

public sealed record ControlActionDefinition(string Id, string Group, string Label);

public static class ControlActions
{
    public static IReadOnlyList<ControlActionDefinition> All { get; } = new List<ControlActionDefinition>
    {
        new("home.score.plus1","Score","Home score +1"),
        new("home.score.plus2","Score","Home score +2"),
        new("home.score.plus3","Score","Home score +3"),
        new("home.score.plus6","Score","Home touchdown +6"),
        new("home.score.minus1","Score","Home score −1"),
        new("home.score.minus2","Score","Home score −2"),
        new("home.score.minus3","Score","Home score −3"),
        new("home.score.minus6","Score","Home touchdown −6"),
        new("away.score.plus1","Score","Away score +1"),
        new("away.score.plus2","Score","Away score +2"),
        new("away.score.plus3","Score","Away score +3"),
        new("away.score.plus6","Score","Away touchdown +6"),
        new("away.score.minus1","Score","Away score −1"),
        new("away.score.minus2","Score","Away score −2"),
        new("away.score.minus3","Score","Away score −3"),
        new("away.score.minus6","Score","Away touchdown −6"),
        new("home.timeout","Score","Home timeout"),
        new("away.timeout","Score","Away timeout"),
        new("possession.toggle","Score","Toggle possession"),
        new("clock.toggle","Clock","Start / stop clock"),
        new("clock.minus60","Clock","Clock −1 minute"),
        new("clock.plus60","Clock","Clock +1 minute"),
        new("clock.minus1","Clock","Clock −1 second"),
        new("clock.plus1","Clock","Clock +1 second"),
        new("clock.set","Clock","Set clock"),
        new("period.next","Clock","Next period"),
        new("football.down.next","Football","Next down"),
        new("football.down.previous","Football","Previous down"),
        new("football.distance.minus","Football","Distance −1 yard"),
        new("football.distance.plus","Football","Distance +1 yard"),
        new("football.ball.minus","Football","Ball spot −1 yard"),
        new("football.ball.plus","Football","Ball spot +1 yard"),
        new("football.series.new","Football","New series"),
        new("football.yards.home","Football","Home +10 yards"),
        new("football.yards.away","Football","Away +10 yards"),
        new("football.turnover.home","Football","Home turnover"),
        new("football.turnover.away","Football","Away turnover"),
        new("output.publish","Football","Update Output"),
        new("basketball.foul.home","Basketball","Home foul"),
        new("basketball.foul.away","Basketball","Away foul"),
        new("basketball.shotclock.toggle","Basketball","Shot clock start / stop"),
        new("basketball.shotclock.reset","Basketball","Shot clock reset 24"),
        new("basketball.shotclock.set","Basketball","Set shot clock"),
        new("baseball.ball","Baseball","Ball"),
        new("baseball.strike","Baseball","Strike"),
        new("baseball.foul","Baseball","Foul ball"),
        new("baseball.out","Baseball","Out"),
        new("baseball.hit.home","Baseball","Home hit"),
        new("baseball.hit.away","Baseball","Away hit"),
        new("soccer.yellow.home","Soccer","Home yellow card"),
        new("soccer.yellow.away","Soccer","Away yellow card"),
        new("soccer.red.home","Soccer","Home red card"),
        new("soccer.red.away","Soccer","Away red card"),
        new("soccer.shot.home","Soccer","Home shot"),
        new("soccer.shot.away","Soccer","Away shot"),
        new("soccer.corner.home","Soccer","Home corner"),
        new("soccer.corner.away","Soccer","Away corner"),
        new("global.undo","General","Undo last action")
    };

    public static string LabelFor(string id) => All.FirstOrDefault(a => a.Id == id)?.Label ?? id;
}

public sealed class KeybindSettings
{
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public KeybindSettings Clone() => new() { Bindings = new Dictionary<string, string>(Bindings, StringComparer.OrdinalIgnoreCase) };
}

public static class KeybindStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Scoreboard", "keybinds.json");

    public static KeybindSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<KeybindSettings>(File.ReadAllText(FilePath)) ?? Defaults()
                : Defaults();
        }
        catch { return new(); }
    }

    public static void Save(KeybindSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }

    public static string ToText(Key key, ModifierKeys modifiers)
    {
        var text = "";
        if ((modifiers & ModifierKeys.Control) != 0) text += "Ctrl+";
        if ((modifiers & ModifierKeys.Shift) != 0) text += "Shift+";
        if ((modifiers & ModifierKeys.Alt) != 0) text += "Alt+";
        if ((modifiers & ModifierKeys.Windows) != 0) text += "Win+";
        return text + KeyName(key);
    }

    public static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemTilde => "`",
        Key.Back => "Backspace",
        Key.Escape => "Esc",
        Key.Return => "Enter",
        _ => key.ToString()
    };

    public static bool TryParse(string text, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None; modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }
        var name = parts[^1];
        if (name.Length == 1 && char.IsDigit(name[0])) { key = (Key)((int)Key.D0 + (name[0] - '0')); return true; }
        switch (name)
        {
            case "-": key = Key.OemMinus; return true;
            case "=": key = Key.OemPlus; return true;
            case ",": key = Key.OemComma; return true;
            case ".": key = Key.OemPeriod; return true;
            case "/": key = Key.OemQuestion; return true;
            case ";": key = Key.OemSemicolon; return true;
            case "'": key = Key.OemQuotes; return true;
            case "[": key = Key.OemOpenBrackets; return true;
            case "]": key = Key.OemCloseBrackets; return true;
            case "\\": key = Key.OemPipe; return true;
            case "`": key = Key.OemTilde; return true;
            case "Backspace": key = Key.Back; return true;
            case "Esc": key = Key.Escape; return true;
            case "Enter": key = Key.Return; return true;
        }
        if (string.Equals(name, "Space", StringComparison.OrdinalIgnoreCase)) { key = Key.Space; return true; }
        if (!Enum.TryParse<Key>(name, true, out var parsed)) return false;
        key = parsed;
        return true;
    }

    public static string? Match(KeybindSettings settings, Key key, ModifierKeys modifiers)
    {
        foreach (var (actionId, gesture) in settings.Bindings)
        {
            if (!TryParse(gesture, out var boundKey, out var boundModifiers)) continue;
            if (boundKey == key && boundModifiers == modifiers) return actionId;
        }
        return null;
    }

    public static KeybindSettings Defaults()
    {
        var settings = new KeybindSettings();
        settings.Bindings["clock.toggle"] = "Space";
        settings.Bindings["football.down.next"] = "F1";
        settings.Bindings["football.down.previous"] = "F2";
        settings.Bindings["output.publish"] = "F5";
        settings.Bindings["global.undo"] = "Ctrl+Z";
        return settings;
    }
}
