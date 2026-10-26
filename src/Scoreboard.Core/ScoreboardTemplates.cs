using System.Text.RegularExpressions;

namespace Scoreboard.Core;

public static class ScoreboardTemplates
{
    static readonly Regex StatToken = new(@"\{\{STAT:(?<name>[^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Bind(string content, GameState state) => StatToken.Replace(ReplaceFixed(content, state), match =>
    {
        var name = match.Groups["name"].Value.Trim();
        var stat = state.Stats.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        return stat is null ? string.Empty : stat.Value.ToString();
    });

    static string ReplaceFixed(string content, GameState state) => content
        // Team names render exactly as the operator typed them: full text, original case.
        .Replace("{{HOME_TEAM}}", state.HomeTeam)
        .Replace("{{AWAY_TEAM}}", state.AwayTeam)
        .Replace("{{HOME_ABBR}}", state.HomeAbbreviation)
        .Replace("{{AWAY_ABBR}}", state.AwayAbbreviation)
        .Replace("{{HOME_SCORE}}", state.HomeScore.ToString())
        .Replace("{{AWAY_SCORE}}", state.AwayScore.ToString())
        .Replace("{{PERIOD}}", PeriodDisplay.Format(state).ToUpperInvariant())
        .Replace("{{GAME_CLOCK}}", GameEngine.FormatClock(state.ClockSeconds))
        .Replace("{{PLAY_CLOCK}}", state.PlayClockSeconds.ToString())
        .Replace("{{TIMEOUT_CLOCK}}", GameEngine.FormatClock(state.TimeoutClockSeconds))
        .Replace("{{TIMEOUTS}}", $"TOL {state.HomeTimeouts}–{state.AwayTimeouts}")
        .Replace("{{POSSESSION}}", state.HomePossession ? state.HomeAbbreviation : state.AwayAbbreviation)
        .Replace("{{DOWN}}", $"{state.Down}{Ordinal(state.Down)}")
        .Replace("{{TO_GO}}", state.YardsToGo.ToString())
        .Replace("{{BALL_ON}}", state.YardsToGoal.ToString())
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
