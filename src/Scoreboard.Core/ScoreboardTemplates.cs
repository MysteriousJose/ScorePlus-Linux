namespace Scoreboard.Core;

public static class ScoreboardTemplates
{
    public static string Bind(string content, GameState state) => content
        .Replace("{{HOME_TEAM}}", state.HomeAbbreviation)
        .Replace("{{AWAY_TEAM}}", state.AwayAbbreviation)
        .Replace("{{HOME_SCORE}}", state.HomeScore.ToString())
        .Replace("{{AWAY_SCORE}}", state.AwayScore.ToString())
        .Replace("{{PERIOD}}", $"{state.PeriodLabel.ToUpperInvariant()} {state.Period}")
        .Replace("{{GAME_CLOCK}}", GameEngine.FormatClock(state.ClockSeconds))
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
