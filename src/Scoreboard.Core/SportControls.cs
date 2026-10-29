namespace Scoreboard.Core;

/// <summary>What a sport-specific control holds: a number the operator steps, or an on/off state.</summary>
public enum SportControlKind { Counter, Flag }

/// <summary>
/// One value a sport needs on the console that football's down and distance does not cover:
/// balls, strikes and outs, bases, power play, overs, wickets, sets and so on. A control belongs to
/// a sport profile, so each sport ships the ones it uses and a custom sport can define its own.
/// </summary>
public sealed record SportControl(
    string Id,
    string Label,
    SportControlKind Kind = SportControlKind.Counter,
    int Min = 0,
    int Max = 99,
    int Step = 1,
    int Default = 0,
    string? OnText = null,
    string? OffText = null)
{
    /// <summary>What the graphic prints for this control at a given value.</summary>
    public string Display(int value) => Kind == SportControlKind.Flag
        ? (value != 0 ? OnText ?? "ON" : OffText ?? string.Empty)
        : value.ToString();

    /// <summary>A flag's two button captions, for the console and the remote.</summary>
    public string FlagOffLabel => OffText is { Length: > 0 } off ? off : "OFF";
    public string FlagOnLabel => OnText is { Length: > 0 } on ? on : "ON";
}

/// <summary>
/// The shipped control sets, one per built-in sport. They are the profile defaults; an operator can
/// change the starting value of any of them in Sport Profiles, and a new game copies the profile's
/// controls onto the game so an edited profile never rewrites a game that is already running.
/// </summary>
public static class SportControls
{
    static readonly Dictionary<string, SportControl[]> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["football"] =
        [
            new("football.firstdowns.home", "Home 1st downs", Max: 99),
            new("football.firstdowns.away", "Visitors 1st downs", Max: 99),
            new("football.redzone", "Red zone", SportControlKind.Flag, OnText: "RED ZONE")
        ],
        ["baseball"] =
        [
            new("baseball.half", "Half inning", SportControlKind.Flag, OnText: "BOTTOM", OffText: "TOP", Default: 0),
            new("baseball.balls", "Balls", Max: 3),
            new("baseball.strikes", "Strikes", Max: 2),
            new("baseball.outs", "Outs", Max: 2),
            new("baseball.first", "Runner on 1st", SportControlKind.Flag, OnText: "1B"),
            new("baseball.second", "Runner on 2nd", SportControlKind.Flag, OnText: "2B"),
            new("baseball.third", "Runner on 3rd", SportControlKind.Flag, OnText: "3B"),
            new("baseball.hits.home", "Home hits", Max: 99),
            new("baseball.hits.away", "Visitors hits", Max: 99),
            new("baseball.errors.home", "Home errors", Max: 99),
            new("baseball.errors.away", "Visitors errors", Max: 99)
        ],
        ["basketball"] =
        [
            new("basketball.fouls.home", "Home fouls", Max: 99),
            new("basketball.fouls.away", "Visitors fouls", Max: 99),
            new("basketball.bonus.home", "Home in the bonus", SportControlKind.Flag, OnText: "BONUS"),
            new("basketball.bonus.away", "Visitors in the bonus", SportControlKind.Flag, OnText: "BONUS"),
            new("basketball.points.inpaint.home", "Home points in paint", Max: 999),
            new("basketball.points.inpaint.away", "Visitors points in paint", Max: 999)
        ],
        ["hockey"] =
        [
            new("hockey.powerplay", "Power play", SportControlKind.Flag, OnText: "PP"),
            new("hockey.shots.home", "Home shots", Max: 99),
            new("hockey.shots.away", "Visitors shots", Max: 99),
            new("hockey.penalty.home", "Home penalty minutes", Max: 99),
            new("hockey.penalty.away", "Visitors penalty minutes", Max: 99),
            new("hockey.empty.net", "Empty net", SportControlKind.Flag, OnText: "EN")
        ],
        ["soccer"] =
        [
            new("soccer.shots.home", "Home shots", Max: 99),
            new("soccer.shots.away", "Visitors shots", Max: 99),
            new("soccer.corners.home", "Home corners", Max: 99),
            new("soccer.corners.away", "Visitors corners", Max: 99),
            new("soccer.saves.home", "Home saves", Max: 99),
            new("soccer.saves.away", "Visitors saves", Max: 99),
            new("soccer.offsides.home", "Home offsides", Max: 99),
            new("soccer.offsides.away", "Visitors offsides", Max: 99),
            new("soccer.yellow.home", "Home yellow cards", Max: 99),
            new("soccer.yellow.away", "Visitors yellow cards", Max: 99),
            new("soccer.red.home", "Home red cards", Max: 99),
            new("soccer.red.away", "Visitors red cards", Max: 99)
        ],
        ["lacrosse"] =
        [
            new("lacrosse.shots.home", "Home shots", Max: 99),
            new("lacrosse.shots.away", "Visitors shots", Max: 99),
            new("lacrosse.saves.home", "Home saves", Max: 99),
            new("lacrosse.saves.away", "Visitors saves", Max: 99),
            new("lacrosse.faceoffs.home", "Home faceoff wins", Max: 99),
            new("lacrosse.faceoffs.away", "Visitors faceoff wins", Max: 99),
            new("lacrosse.powerplay", "Power play", SportControlKind.Flag, OnText: "PP"),
            new("lacrosse.penalty.home", "Home penalty minutes", Max: 99),
            new("lacrosse.penalty.away", "Visitors penalty minutes", Max: 99)
        ],
        ["rugby"] =
        [
            new("rugby.tries.home", "Home tries", Max: 99),
            new("rugby.tries.away", "Visitors tries", Max: 99),
            new("rugby.conversions.home", "Home conversions", Max: 99),
            new("rugby.conversions.away", "Visitors conversions", Max: 99),
            new("rugby.penalties.home", "Home penalty goals", Max: 99),
            new("rugby.penalties.away", "Visitors penalty goals", Max: 99),
            new("rugby.yellow.home", "Home yellow cards", Max: 99),
            new("rugby.yellow.away", "Visitors yellow cards", Max: 99),
            new("rugby.red.home", "Home red cards", Max: 99),
            new("rugby.red.away", "Visitors red cards", Max: 99)
        ],
        ["cricket"] =
        [
            new("cricket.overs", "Overs bowled", Max: 200),
            new("cricket.wickets", "Wickets", Max: 10),
            new("cricket.extras", "Extras", Max: 999),
            new("cricket.target", "Target", Max: 999),
            new("cricket.daynight", "Day / night", SportControlKind.Flag, OnText: "DAY/NIGHT"),
            new("cricket.inningsbreak", "Innings break", SportControlKind.Flag, OnText: "INNINGS BREAK")
        ],
        ["tennis"] =
        [
            new("tennis.sets.home", "Home sets", Max: 5),
            new("tennis.sets.away", "Visitors sets", Max: 5),
            new("tennis.games.home", "Home games", Max: 99),
            new("tennis.games.away", "Visitors games", Max: 99),
            new("tennis.serve", "Serve", SportControlKind.Flag, OnText: "HOME SERVES", OffText: "VISITORS SERVE"),
            new("tennis.tiebreak", "Tiebreak", SportControlKind.Flag, OnText: "TIEBREAK")
        ],
        ["volleyball"] =
        [
            new("volleyball.sets.home", "Home sets", Max: 5),
            new("volleyball.sets.away", "Visitors sets", Max: 5),
            new("volleyball.serve", "Serve", SportControlKind.Flag, OnText: "HOME SERVES", OffText: "VISITORS SERVE"),
            new("volleyball.setpoint", "Set point", SportControlKind.Flag, OnText: "SET POINT")
        ],
        ["wrestling"] =
        [
            new("wrestling.nearfall.home", "Home near fall", Max: 99),
            new("wrestling.nearfall.away", "Visitors near fall", Max: 99),
            new("wrestling.ridingtime.home", "Home riding time", Max: 99),
            new("wrestling.ridingtime.away", "Visitors riding time", Max: 99),
            new("wrestling.bloodtime.home", "Home blood time", Max: 99),
            new("wrestling.bloodtime.away", "Visitors blood time", Max: 99),
            new("wrestling.overtime", "Overtime", SportControlKind.Flag, OnText: "OVERTIME")
        ],
        ["australian-football"] =
        [
            new("afl.behinds.home", "Home behinds", Max: 99),
            new("afl.behinds.away", "Visitors behinds", Max: 99),
            new("afl.tackles.home", "Home tackles", Max: 99),
            new("afl.tackles.away", "Visitors tackles", Max: 99),
            new("afl.inside50.home", "Home inside 50s", Max: 99),
            new("afl.inside50.away", "Visitors inside 50s", Max: 99),
            new("afl.freekicks.home", "Home free kicks", Max: 99),
            new("afl.freekicks.away", "Visitors free kicks", Max: 99)
        ],
        ["generic"] = []
    };

    /// <summary>The shipped controls for a sport, or none when it is a custom profile.</summary>
    public static IReadOnlyList<SportControl> For(string? sportId) =>
        sportId is not null && BuiltIn.TryGetValue(sportId, out var controls) ? controls : [];

    /// <summary>Every shipped control, so keybinds can offer them all.</summary>
    public static IEnumerable<SportControl> All => BuiltIn.Values.SelectMany(c => c);
}

/// <summary>
/// Reads and writes a sport control's value. The long-standing fields — balls, strikes, fouls,
/// cards — stay where they are so existing saves, templates and reports keep working; anything new
/// lives in <see cref="GameState.SportValues"/>.
/// </summary>
public static class SportValues
{
    public static int Read(GameState state, string? id) => (id ?? string.Empty).ToLowerInvariant() switch
    {
        "baseball.balls" => state.Balls,
        "baseball.strikes" => state.Strikes,
        "baseball.outs" => state.Outs,
        "baseball.hits.home" => state.HomeHits,
        "baseball.hits.away" => state.AwayHits,
        "basketball.fouls.home" => state.HomeFouls,
        "basketball.fouls.away" => state.AwayFouls,
        "soccer.shots.home" => state.HomeShots,
        "soccer.shots.away" => state.AwayShots,
        "soccer.corners.home" => state.HomeCorners,
        "soccer.corners.away" => state.AwayCorners,
        "soccer.yellow.home" => state.HomeYellowCards,
        "soccer.yellow.away" => state.AwayYellowCards,
        "soccer.red.home" => state.HomeRedCards,
        "soccer.red.away" => state.AwayRedCards,
        _ => state.SportValues.TryGetValue(id ?? string.Empty, out var stored) ? stored : 0
    };

    public static void Write(GameState state, string? id, int value)
    {
        switch ((id ?? string.Empty).ToLowerInvariant())
        {
            case "baseball.balls": state.Balls = value; return;
            case "baseball.strikes": state.Strikes = value; return;
            case "baseball.outs": state.Outs = value; return;
            case "baseball.hits.home": state.HomeHits = value; return;
            case "baseball.hits.away": state.AwayHits = value; return;
            case "basketball.fouls.home": state.HomeFouls = value; return;
            case "basketball.fouls.away": state.AwayFouls = value; return;
            case "soccer.shots.home": state.HomeShots = value; return;
            case "soccer.shots.away": state.AwayShots = value; return;
            case "soccer.corners.home": state.HomeCorners = value; return;
            case "soccer.corners.away": state.AwayCorners = value; return;
            case "soccer.yellow.home": state.HomeYellowCards = value; return;
            case "soccer.yellow.away": state.AwayYellowCards = value; return;
            case "soccer.red.home": state.HomeRedCards = value; return;
            case "soccer.red.away": state.AwayRedCards = value; return;
            default:
                if (!string.IsNullOrWhiteSpace(id)) state.SportValues[id] = value;
                return;
        }
    }

    /// <summary>What a control prints right now, with a flag's words instead of 1 and 0.</summary>
    public static string Display(GameState state, SportControl control) => control.Display(Read(state, control.Id));

    /// <summary>Finds the control by id on this game, then across every shipped sport.</summary>
    public static SportControl? FindControl(GameState state, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var onGame = state.Controls.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (onGame is not null) return onGame;
        var known = SportControls.For(state.SportId).FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        return known ?? SportControls.All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
