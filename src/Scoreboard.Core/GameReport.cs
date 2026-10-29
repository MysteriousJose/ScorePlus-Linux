using System.Net;
using System.Text;

namespace Scoreboard.Core;

public static class GameReport
{
    public static string Build(GameState state)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Game Report</title><style>");
        html.AppendLine("body{font:15px Segoe UI,Arial,sans-serif;max-width:900px;margin:36px auto;color:#17212b}h1{margin-bottom:4px}.muted{color:#64748b}.score{font-size:32px;font-weight:bold;margin:20px 0}.cards{display:grid;grid-template-columns:1fr 1fr;gap:12px}.card{border:1px solid #cbd5e1;padding:14px;border-radius:8px}table{border-collapse:collapse;width:100%;margin-top:16px}th,td{text-align:left;padding:8px;border-bottom:1px solid #d7dee7}th{background:#edf2f7}@media print{body{margin:0}button{display:none}}");
        html.AppendLine("</style></head><body>");
        html.AppendLine($"<h1>{E(state.HomeTeam)} vs {E(state.AwayTeam)}</h1><div class=\"muted\">{E(state.SportId.ToUpperInvariant())} · Game report · generated {DateTime.Now:yyyy-MM-dd HH:mm}</div>");
        html.AppendLine($"<div class=\"score\">{E(state.HomeAbbreviation)} {state.HomeScore} — {state.AwayScore} {E(state.AwayAbbreviation)}</div>");
        html.AppendLine("<div class=\"cards\">");
        html.AppendLine($"<div class=\"card\"><b>Game</b><br/>{E(PeriodDisplay.Format(state))} of {state.TotalPeriods}<br/>Clock: {GameEngine.FormatClock(state.ClockSeconds)}<br/>Timeouts: {state.HomeTimeouts} — {state.AwayTimeouts}</div>");
        if(state.SportId=="football")html.AppendLine($"<div class=\"card\"><b>Football</b><br/>{state.Down}{Ordinal(state.Down)} &amp; {state.YardsToGo}<br/>Ball at {state.YardsToGoal} yard line<br/>Total yards: {E(state.HomeTeam)} {state.HomeYards} — {state.AwayYards} {E(state.AwayTeam)}<br/>Turnovers: {state.HomeTurnovers} — {state.AwayTurnovers}</div>");
        if(state.SportId=="basketball")html.AppendLine($"<div class=\"card\"><b>Basketball</b><br/>Fouls: {state.HomeFouls} — {state.AwayFouls}<br/>Shot clock: {state.ShotClockSeconds}s<br/>Possession: {E(state.HomePossession?state.HomeTeam:state.AwayTeam)}</div>");
        if(state.SportId=="baseball")html.AppendLine($"<div class=\"card\"><b>Baseball</b><br/>Count: {state.Balls} balls, {state.Strikes} strikes, {state.Outs} outs<br/>Hits: {E(state.HomeTeam)} {state.HomeHits} — {state.AwayHits} {E(state.AwayTeam)}</div>");
        if(state.SportId=="soccer")html.AppendLine($"<div class=\"card\"><b>Soccer</b><br/>Cards (yellow/red): {state.HomeYellowCards}/{state.HomeRedCards} — {state.AwayYellowCards}/{state.AwayRedCards}<br/>Shots: {state.HomeShots} — {state.AwayShots}<br/>Corners: {state.HomeCorners} — {state.AwayCorners}</div>");
        if(state.Stats is { Count: > 0 })
        {
            html.AppendLine("<div class=\"card\"><b>Custom stats</b><br/>");
            foreach(var stat in state.Stats) html.AppendLine($"{E(stat.Name)}: {E(stat.Display)}<br/>");
            html.AppendLine("</div>");
        }
        html.AppendLine("</div><h2>Event timeline</h2><table><thead><tr><th>Time</th><th>Score at event</th><th>Action</th></tr></thead><tbody>");
        foreach(var gameEvent in state.Events)
            html.AppendLine($"<tr><td>{gameEvent.At:yyyy-MM-dd HH:mm:ss}</td><td>{gameEvent.HomeScore?.ToString() ?? "—"} — {gameEvent.AwayScore?.ToString() ?? "—"}</td><td><b>{E(gameEvent.Type)}</b> · {E(gameEvent.Description)}</td></tr>");
        html.AppendLine("</tbody></table><p class=\"muted\">Use your browser's Print command to print or save this report as PDF.</p></body></html>");
        return html.ToString();
    }
    static string Ordinal(int value)=>value switch{1=>"st",2=>"nd",3=>"rd",_=>"th"};
}
