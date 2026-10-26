using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

public sealed record SportDefinition(string Id, string Name, string Unit, int Periods, int PeriodSeconds, int[] ScoringOptions, bool HasShotClock = false, bool CountUp = false, int Timeouts = 5)
{
    public static IReadOnlyList<SportDefinition> BuiltIns { get; } = new[]
    {
        new SportDefinition("basketball", "Basketball", "Quarter", 4, 12 * 60, [1, 2, 3], true, false, 5),
        new SportDefinition("football", "American Football", "Quarter", 4, 15 * 60, [1, 2, 3, 6, 7, 8], true, false, 3),
        new SportDefinition("baseball", "Baseball", "Inning", 9, 0, [1], false, true, 3),
        new SportDefinition("soccer", "Soccer", "Half", 2, 45 * 60, [1], false, false, 5),
        new SportDefinition("hockey", "Hockey", "Period", 3, 20 * 60, [1], true, false, 1),
        new SportDefinition("lacrosse", "Lacrosse", "Quarter", 4, 12 * 60, [1], true, false, 3),
        new SportDefinition("volleyball", "Volleyball", "Set", 5, 0, [1], false, true, 2),
        new SportDefinition("tennis", "Tennis", "Set", 3, 0, [1], false, true, 3),
        new SportDefinition("generic", "Generic", "Period", 2, 10 * 60, [1, 2, 3], false, false, 3)
    };
}

/// <summary>A single user-defined statistic such as Turnovers or Penalties.</summary>
public sealed record GameStat(string Id, string Name, int Value = 0);

public static class CommonStats
{
    /// <summary>Suggested stat names offered when adding a custom stat.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "Turnovers", "Penalties", "Penalty yards", "Shots", "Shots on goal", "Saves", "Corners",
        "Rebounds", "Assists", "Steals", "Blocks", "Fouls", "Hits", "Errors", "Faceoff wins", "Possessions"
    ];
}

public sealed class GameState
{
    public string SportId { get; set; } = "basketball";
    public string HomeTeam { get; set; } = "HOME";
    public string AwayTeam { get; set; } = "VISITORS";
    public string HomeAbbreviation { get; set; } = "HOME";
    public string AwayAbbreviation { get; set; } = "AWAY";
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int Period { get; set; } = 1;
    public int HomeTimeouts { get; set; } = 5;
    public int AwayTimeouts { get; set; } = 5;
    public int HomeFouls { get; set; }
    public int AwayFouls { get; set; }
    public bool HomePossession { get; set; } = true;
    public int ClockSeconds { get; set; } = 720;
    public int PeriodSeconds { get; set; } = 720;
    public int TotalPeriods { get; set; } = 4;
    public string PeriodLabel { get; set; } = "Quarter";
    public int[] ScoringOptions { get; set; } = [1, 2, 3];
    public int ShotClockSeconds { get; set; } = 30;
    public int Down { get; set; } = 1;
    public string QuarterDisplayFormat { get; set; } = "Full";
    public int YardsToGo { get; set; } = 10;
    public int YardsToGoal { get; set; } = 75;
    public int HomeYellowCards { get; set; }
    public int AwayYellowCards { get; set; }
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }
    public int Balls { get; set; }
    public int Strikes { get; set; }
    public int Outs { get; set; }
    public int HomeHits { get; set; }
    public int AwayHits { get; set; }
    public int HomeYards { get; set; }
    public int AwayYards { get; set; }
    public int HomeTurnovers { get; set; }
    public int AwayTurnovers { get; set; }
    public int HomeShots { get; set; }
    public int AwayShots { get; set; }
    public int HomeCorners { get; set; }
    public int AwayCorners { get; set; }
    public List<GameStat> Stats { get; set; } = [];
    public bool ClockRunning { get; set; }
    public bool ShotClockRunning { get; set; }
    public bool ClockCountsUp { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
    public List<GameEvent> Events { get; set; } = [];
}

public sealed record GameEvent(DateTimeOffset At, string Type, string Description, int? HomeScore = null, int? AwayScore = null);
public sealed record GameSnapshot(GameState State, string Label);

public sealed class GameEngine
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    readonly Stopwatch _watch = Stopwatch.StartNew();
    long _lastTick;
    readonly Stack<GameSnapshot> _undo = new();
    public SportCatalog Sports { get; }
    public GameState State { get; private set; } = new();
    public event Action? Changed;
    public SportDefinition Sport => Sports.Find(State.SportId) ?? SportDefinition.BuiltIns[^1];

    public GameEngine(SportCatalog? sports = null) => Sports = sports ?? new SportCatalog();

    public void NewGame(string sportId)
    {
        var sport = Sports.Find(sportId) ?? SportDefinition.BuiltIns[^1];
        State = new GameState { SportId = sport.Id, ClockSeconds = sport.PeriodSeconds, PeriodSeconds = sport.PeriodSeconds, TotalPeriods = sport.Periods, PeriodLabel = sport.Unit, ScoringOptions = sport.ScoringOptions.ToArray(), ClockCountsUp = sport.CountUp, HomeTimeouts = sport.Timeouts, AwayTimeouts = sport.Timeouts };
        _undo.Clear(); _lastTick = _watch.ElapsedTicks; Changed?.Invoke();
    }

    public void SetTeams(string home, string away)
    {
        SaveUndo("Teams"); State.HomeTeam = home; State.AwayTeam = away;
        State.HomeAbbreviation = Abbreviate(home); State.AwayAbbreviation = Abbreviate(away); Log("Teams", $"{home} vs {away}"); Changed?.Invoke();
    }

    public void Score(bool home, int amount)
    {
        SaveUndo("Score"); if (home) State.HomeScore = Math.Max(0, State.HomeScore + amount); else State.AwayScore = Math.Max(0, State.AwayScore + amount);
        Log("Score", $"{(home ? State.HomeTeam : State.AwayTeam)} {(amount >= 0 ? "+" : "−")}{Math.Abs(amount)}"); Changed?.Invoke();
    }

    public void SetScore(bool home, int value)
    {
        SaveUndo("Score correction"); if (home) State.HomeScore = Math.Max(0, value); else State.AwayScore = Math.Max(0, value);
        Log("Score", $"{(home ? State.HomeTeam : State.AwayTeam)} score set to {Math.Max(0, value)}"); Changed?.Invoke();
    }

    public void ToggleClock()
    {
        Tick(); SaveUndo("Clock"); State.ClockRunning = !State.ClockRunning; _lastTick = _watch.ElapsedTicks;
        Log("Clock", State.ClockRunning ? "Clock started" : "Clock stopped"); Changed?.Invoke();
    }

    public void SetClock(int seconds)
    {
        Tick(); SaveUndo("Clock correction"); State.ClockSeconds = Math.Max(0, seconds); _lastTick = _watch.ElapsedTicks;
        Log("Clock", $"Clock set to {FormatClock(State.ClockSeconds)}"); Changed?.Invoke();
    }

    public void AdjustClock(int seconds) => SetClock(State.ClockSeconds + seconds);

    public void Tick()
    {
        var now = _watch.ElapsedTicks;
        var elapsed = (int)((now - _lastTick) / Stopwatch.Frequency);
        if (elapsed <= 0) return;
        _lastTick += (long)elapsed * Stopwatch.Frequency;
        var wasRunning = State.ClockRunning || State.ShotClockRunning;
        if (State.ClockRunning)
        {
            State.ClockSeconds = State.ClockCountsUp ? State.ClockSeconds + elapsed : Math.Max(0, State.ClockSeconds - elapsed);
            if (State.ClockSeconds == 0 && !State.ClockCountsUp) { State.ClockRunning = false; Log("Clock", "Period ended"); }
        }
        if (State.ShotClockRunning)
        {
            State.ShotClockSeconds = Math.Max(0, State.ShotClockSeconds - elapsed);
            if (State.ShotClockSeconds == 0) { State.ShotClockRunning = false; Log("Shot clock", "Shot clock expired"); }
        }
        // Only notify the UI when a running clock actually advanced, so idle ticks cost nothing.
        if (wasRunning || State.ClockRunning || State.ShotClockRunning) Changed?.Invoke();
    }

    public void NextPeriod()
    {
        SaveUndo("Period"); State.Period++; State.ClockSeconds = State.PeriodSeconds; State.ClockRunning = false;
        Log("Period", $"{State.PeriodLabel} {State.Period}"); Changed?.Invoke();
    }

    public void TogglePossession() { SaveUndo("Possession"); State.HomePossession = !State.HomePossession; Log("Possession", $"{(State.HomePossession ? State.HomeTeam : State.AwayTeam)} possession"); Changed?.Invoke(); }
    public void AdvanceDown() { if (State.SportId != "football") return; SaveUndo("Down"); if (State.Down >= 4) { State.Down=1; State.YardsToGo=10; State.YardsToGoal=Math.Max(0,State.YardsToGoal-State.YardsToGo); } else State.Down++; Log("Down", $"Down {State.Down}, {State.YardsToGo} to go"); Changed?.Invoke(); }
    public void PreviousDown() { if (State.SportId != "football") return; SaveUndo("Previous down"); if (State.Down > 1) State.Down--; Log("Down", $"Down {State.Down}, {State.YardsToGo} to go"); Changed?.Invoke(); }
    public void NewSeries() { if (State.SportId != "football") return; SaveUndo("New series"); State.Down = 1; State.YardsToGo = 10; Log("Series", "New series · 1st & 10"); Changed?.Invoke(); }
    public void SetPeriodFormat(string format) { if(format is not ("Full" or "Short" or "Number"))return; if(State.QuarterDisplayFormat==format)return; SaveUndo("Period format"); State.QuarterDisplayFormat=format; Log("Period", $"Period display: {PeriodDisplay.Format(State)}"); Changed?.Invoke(); }
    public void SetFootballDistance(int yardsToGo,int? yardsToGoal=null) { if (State.SportId != "football") return; SaveUndo("Football distance"); State.YardsToGo=Math.Clamp(yardsToGo,1,99); if(yardsToGoal is not null)State.YardsToGoal=Math.Clamp(yardsToGoal.Value,0,99); Log("Yards", $"{State.YardsToGo} to go, ball at {State.YardsToGoal}"); Changed?.Invoke(); }
    public void AddCard(bool home,bool red=false) { if(State.SportId!="soccer")return; SaveUndo(red?"Red card":"Yellow card"); if(red){if(home)State.HomeRedCards++;else State.AwayRedCards++;}else{if(home)State.HomeYellowCards++;else State.AwayYellowCards++;} Log("Card",$"{(home?State.HomeTeam:State.AwayTeam)} {(red?"red":"yellow")} card");Changed?.Invoke(); }
    public void BaseballBall() { if(State.SportId!="baseball")return; SaveUndo("Ball"); State.Balls++; if(State.Balls>=4){State.Balls=0;State.Strikes=0;Log("Count","Walk · count reset");}else Log("Count","Ball");Changed?.Invoke(); }
    public void BaseballStrike(bool foul=false) { if(State.SportId!="baseball")return; SaveUndo(foul?"Foul ball":"Strike"); if(!foul||State.Strikes<2)State.Strikes++; if(State.Strikes>=3){State.Outs++;State.Balls=0;State.Strikes=0;Log("Count","Strikeout · batter out");}else Log("Count",foul?"Foul ball":"Strike");Changed?.Invoke(); }
    public void BaseballOut() { if(State.SportId!="baseball")return; SaveUndo("Out"); State.Outs++; State.Balls=0; State.Strikes=0; Log("Out","Out recorded · count reset");Changed?.Invoke(); }
    public void BaseballHit(bool home) { if(State.SportId!="baseball")return; SaveUndo("Hit"); if(home)State.HomeHits++;else State.AwayHits++; State.Balls=0;State.Strikes=0;Log("Hit",$"{(home?State.HomeTeam:State.AwayTeam)} hit");Changed?.Invoke(); }
    public void Timeout(bool home) { SaveUndo("Timeout"); if (home) State.HomeTimeouts = Math.Max(0, State.HomeTimeouts - 1); else State.AwayTimeouts = Math.Max(0, State.AwayTimeouts - 1); Log("Timeout", $"{(home ? State.HomeTeam : State.AwayTeam)} timeout"); Changed?.Invoke(); }
    public void AddFoul(bool home) { if(State.SportId!="basketball")return; SaveUndo("Foul"); if (home) State.HomeFouls++; else State.AwayFouls++; Log("Foul", $"{(home ? State.HomeTeam : State.AwayTeam)} foul"); Changed?.Invoke(); }
    public void SetShotClock(int seconds) { if(State.SportId!="basketball")return; SaveUndo("Shot clock");State.ShotClockSeconds=Math.Clamp(seconds,0,99);State.ShotClockRunning=false;Log("Shot clock",$"Shot clock set to {State.ShotClockSeconds}");Changed?.Invoke(); }
    public void ToggleShotClock() { if(State.SportId!="basketball")return;SaveUndo("Shot clock");State.ShotClockRunning=!State.ShotClockRunning;_lastTick=_watch.ElapsedTicks;Log("Shot clock",State.ShotClockRunning?"Shot clock started":"Shot clock stopped");Changed?.Invoke(); }
    public void ResetShotClock(int seconds=24) { if(State.SportId!="basketball")return;SaveUndo("Shot clock reset");State.ShotClockSeconds=Math.Clamp(seconds,1,99);State.ShotClockRunning=false;Log("Shot clock",$"Shot clock reset to {State.ShotClockSeconds}");Changed?.Invoke(); }
    public void AddFootballYards(bool home,int yards) { if(State.SportId!="football")return;SaveUndo("Rushing/receiving yards");if(home)State.HomeYards=Math.Max(0,State.HomeYards+yards);else State.AwayYards=Math.Max(0,State.AwayYards+yards);Log("Yards",$"{(home?State.HomeTeam:State.AwayTeam)} {yards:+#;-#;0} yards");Changed?.Invoke(); }
    public void AddTurnover(bool home) { if(State.SportId!="football")return;SaveUndo("Turnover");if(home)State.HomeTurnovers++;else State.AwayTurnovers++;Log("Turnover",$"{(home?State.HomeTeam:State.AwayTeam)} turnover");Changed?.Invoke(); }
    public void AddSoccerShot(bool home) { if(State.SportId!="soccer")return;SaveUndo("Shot");if(home)State.HomeShots++;else State.AwayShots++;Log("Shot",$"{(home?State.HomeTeam:State.AwayTeam)} shot");Changed?.Invoke(); }
    public void AddSoccerCorner(bool home) { if(State.SportId!="soccer")return;SaveUndo("Corner");if(home)State.HomeCorners++;else State.AwayCorners++;Log("Corner",$"{(home?State.HomeTeam:State.AwayTeam)} corner");Changed?.Invoke(); }

    public GameStat AddStat(string name)
    {
        var trimmed = name.Trim();
        var existing = State.Stats.FirstOrDefault(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        SaveUndo("Stat"); var stat = new GameStat(Guid.NewGuid().ToString("N"), trimmed.Length == 0 ? "Stat" : trimmed);
        State.Stats.Add(stat); Log("Stat", $"Added stat {stat.Name}"); Changed?.Invoke(); return stat;
    }

    public void RemoveStat(string id)
    {
        var stat = State.Stats.FirstOrDefault(s => s.Id == id); if (stat is null) return;
        SaveUndo("Remove stat"); State.Stats.Remove(stat); Log("Stat", $"Removed stat {stat.Name}"); Changed?.Invoke();
    }

    public void AdjustStat(string id, int delta)
    {
        var stat = State.Stats.FirstOrDefault(s => s.Id == id); if (stat is null) return;
        SaveUndo(stat.Name); var index = State.Stats.IndexOf(stat);
        var updated = stat with { Value = Math.Max(0, stat.Value + delta) };
        State.Stats[index] = updated;
        Log(stat.Name, $"{updated.Name}: {updated.Value}"); Changed?.Invoke();
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var snapshot = _undo.Pop(); State = snapshot.State; _lastTick = _watch.ElapsedTicks; Log("Undo", $"Undid {snapshot.Label}"); Changed?.Invoke(); return true;
    }

    public void ResetGame()
    {
        SaveUndo("Game reset"); var home = State.HomeTeam; var away = State.AwayTeam; var id = State.SportId;
        NewGame(id); State.HomeTeam = home; State.AwayTeam = away; State.HomeAbbreviation = Abbreviate(home); State.AwayAbbreviation = Abbreviate(away);
        Log("Game", "Game reset"); Changed?.Invoke();
    }

    public void Save(string path)
    {
        Tick(); State.SavedAt = DateTimeOffset.Now; var json = JsonSerializer.Serialize(State, JsonOptions);
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + ".tmp"; File.WriteAllText(temporary, json); File.Move(temporary, full, true);
    }

    public void Load(string path)
    {
        State = JsonSerializer.Deserialize<GameState>(File.ReadAllText(path), JsonOptions) ?? new GameState();
        _undo.Clear(); _lastTick = _watch.ElapsedTicks; Changed?.Invoke();
    }

    public string ExportJson() => JsonSerializer.Serialize(State, JsonOptions);
    public GameState CloneState() => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(State, JsonOptions), JsonOptions)!;
    public void LoadFromState(GameState state){State=JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state,JsonOptions),JsonOptions)!;_undo.Clear();_lastTick=_watch.ElapsedTicks;Changed?.Invoke();}
    public bool UpdateLiveScoreClock(GameState source)
    {
        if (State.HomeScore == source.HomeScore && State.AwayScore == source.AwayScore &&
            State.ClockSeconds == source.ClockSeconds && State.ClockRunning == source.ClockRunning &&
            State.ClockCountsUp == source.ClockCountsUp && State.ShotClockSeconds == source.ShotClockSeconds &&
            State.ShotClockRunning == source.ShotClockRunning) return false;
        State.HomeScore=source.HomeScore;State.AwayScore=source.AwayScore;
        State.ClockSeconds=source.ClockSeconds;State.ClockRunning=source.ClockRunning;State.ClockCountsUp=source.ClockCountsUp;
        State.ShotClockSeconds=source.ShotClockSeconds;State.ShotClockRunning=source.ShotClockRunning;
        Changed?.Invoke();
        return true;
    }
    void SaveUndo(string label) { _undo.Push(new GameSnapshot(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(State, JsonOptions), JsonOptions)!, label)); if (_undo.Count > 100) { var keep = _undo.Reverse().TakeLast(100).ToArray(); _undo.Clear(); foreach (var x in keep) _undo.Push(x); } }
    void Log(string type, string description) => State.Events.Add(new GameEvent(DateTimeOffset.Now, type, description, State.HomeScore, State.AwayScore));
    static string Abbreviate(string value) => string.Concat(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(s => char.ToUpperInvariant(s[0])));
    public static string FormatClock(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
}
