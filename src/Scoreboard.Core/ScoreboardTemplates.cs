using System.Text.RegularExpressions;

namespace Scoreboard.Core;

public static class ScoreboardTemplates
{
    static readonly Regex StatToken = new(@"\{\{STAT:(?<name>[^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A value read from the player currently selected for one side, e.g.
    /// <c>{{PLAYER:HOME:Name}}</c>, <c>{{PLAYER:AWAY:Number}}</c> or <c>{{PLAYER:HOME:Yards}}</c>.
    /// Switching the selected player in the Rosters tab reprints every one of these.
    /// </summary>
    static readonly Regex PlayerToken = new(@"\{\{PLAYER:(?<side>HOME|AWAY):(?<field>[^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Bind(string content, GameState state, string? periodFormat = null) =>
        PlayerToken.Replace(StatToken.Replace(ReplaceFixed(content, state, periodFormat), match =>
        {
            var name = match.Groups["name"].Value.Trim();
            var stat = state.Stats.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            return stat is null ? string.Empty : stat.Display;
        }), match =>
        {
            var home = match.Groups["side"].Value.Equals("HOME", StringComparison.OrdinalIgnoreCase);
            var field = match.Groups["field"].Value;
            // {{PLAYER:HOME:id:<playerId>:Yards}} pins one player from that side's roster, so an
            // element can show a specific player while somebody else is the selected one.
            if (field.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = field[3..];
                var split = rest.IndexOf(':');
                if (split <= 0) return string.Empty;
                var roster = RosterStore.Book.Find(home ? state.HomeRosterId : state.AwayRosterId);
                var pinned = roster?.Find(rest[..split].Trim());
                return pinned is null ? string.Empty : PlayerFields.Read(pinned, rest[(split + 1)..]);
            }
            var player = home ? state.HomePlayer : state.AwayPlayer;
            return player?.Field(field) ?? string.Empty;
        });

    static string ReplaceFixed(string content, GameState state, string? periodFormat) => content
        // Team names render exactly as the operator typed them: full text, original case.
        .Replace("{{HOME_TEAM}}", state.HomeTeam)
        .Replace("{{AWAY_TEAM}}", state.AwayTeam)
        .Replace("{{HOME_ABBR}}", state.HomeAbbreviation)
        .Replace("{{AWAY_ABBR}}", state.AwayAbbreviation)
        .Replace("{{HOME_SCORE}}", state.HomeScore.ToString())
        .Replace("{{AWAY_SCORE}}", state.AwayScore.ToString())
        .Replace("{{PERIOD}}", PeriodDisplay.Format(state, periodFormat).ToUpperInvariant())
        .Replace("{{GAME_CLOCK}}", GameEngine.FormatClock(state.ClockSeconds))
        .Replace("{{PLAY_CLOCK}}", state.PlayClockSeconds.ToString())
        .Replace("{{TIMEOUT_CLOCK}}", GameEngine.FormatClock(state.TimeoutClockSeconds))
        .Replace("{{TIMEOUTS}}", $"TOL {state.HomeTimeouts}–{state.AwayTimeouts}")
        .Replace("{{POSSESSION}}", state.HomePossession ? state.HomeAbbreviation : state.AwayAbbreviation)
        .Replace("{{DOWN}}", $"{state.Down}{Ordinal(state.Down)}")
        .Replace("{{TO_GO}}", state.YardsToGo.ToString())
        // The ball spot is a plain yard line (0-50) with its own side, independent of possession.
        .Replace("{{BALL_ON}}", state.YardsToGoal.ToString())
        .Replace("{{BALL_SIDE}}", state.BallOnHomeSide ? state.HomeAbbreviation : state.AwayAbbreviation)
        .Replace("{{BALL_TEAM}}", state.BallOnHomeSide ? state.HomeTeam : state.AwayTeam)
        .Replace("{{BALL_SIDE_SPOT}}", $"{(state.BallOnHomeSide ? state.HomeAbbreviation : state.AwayAbbreviation)} {state.YardsToGoal}")
        .Replace("{{DOWNS}}", $"{state.Down}{Ordinal(state.Down)} & {state.YardsToGo}")
        .Replace("{{BALL_SPOT}}", $"BALL {state.YardsToGoal}")
        .Replace("{{SPORT}}", state.SportId.ToUpperInvariant())
        .Replace("{{YARDS_TO_GO}}", state.YardsToGo.ToString())
        .Replace("{{HOME_YARDS}}", state.HomeYards.ToString())
        .Replace("{{AWAY_YARDS}}", state.AwayYards.ToString())
        .Replace("{{SOCCER_CARDS}}", $"Y {state.HomeYellowCards}/{state.AwayYellowCards} · R {state.HomeRedCards}/{state.AwayRedCards}")
        .Replace("{{BASEBALL_OUTS}}", state.Outs.ToString())
        .Replace("{{BASKETBALL_FOULS}}", $"{state.HomeFouls}–{state.AwayFouls}")
        .Replace("{{YARDS}}", $"{state.HomeYards}–{state.AwayYards}")
        .Replace("{{TURNOVERS}}", $"{state.HomeTurnovers}–{state.AwayTurnovers}")
        .Replace("{{FOULS}}", $"{state.HomeFouls}–{state.AwayFouls}")
        .Replace("{{SHOT_CLOCK}}", state.ShotClockSeconds.ToString())
        .Replace("{{BASEBALL_COUNT}}", $"{state.Balls}-{state.Strikes} · {state.Outs} OUT")
        .Replace("{{HITS}}", $"{state.HomeHits}–{state.AwayHits}")
        .Replace("{{CARDS}}", $"Y{state.HomeYellowCards}/{state.AwayYellowCards} R{state.HomeRedCards}/{state.AwayRedCards}")
        .Replace("{{SHOTS}}", $"{state.HomeShots}–{state.AwayShots}")
        .Replace("{{CORNERS}}", $"{state.HomeCorners}–{state.AwayCorners}")
        .Replace("{{DATE}}", DateTime.Now.ToString("yyyy-MM-dd"))
        .Replace("{{TIME}}", DateTime.Now.ToString("HH:mm:ss"));

    static string Ordinal(int value) => value switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
}
