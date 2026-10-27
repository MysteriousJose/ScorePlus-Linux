using System.Text.Json;
using Scoreboard.Core;
// Keep the suite's default paths (sports.json, scorebug.json, autosaves) in a throwaway folder.
var appDataPath=Path.Combine(Path.GetTempPath(),$"core-appdata-{Guid.NewGuid():N}");
AppPaths.UseRoot(appDataPath);
AppDomain.CurrentDomain.ProcessExit+=(_,_)=>{try{Directory.Delete(appDataPath,true);}catch{}};
static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
var engine=new GameEngine();
Check(SportDefinition.BuiltIns.Select(x=>x.Id).Distinct().Count()>=9,"all built-in sports have unique identifiers");
engine.NewGame("basketball"); engine.SetTeams("Eagles","Tigers");
engine.Score(true,3); engine.Score(false,2);
Check(engine.State.HomeScore==3&&engine.State.AwayScore==2,"home and away scoring operations");
Check(engine.Undo()&&engine.State.AwayScore==0,"undo reverses last scoring action");
engine.SetClock(3); engine.State.ClockRunning=true; Thread.Sleep(1200); engine.Tick();
Check(engine.State.ClockSeconds<=2&&engine.State.ClockSeconds>=1,"countdown clock uses elapsed monotonic time");
engine.State.ClockRunning=false; engine.SetClock(0); engine.State.ClockRunning=true; Thread.Sleep(1100); engine.Tick();
Check(engine.State.ClockSeconds==0&&!engine.State.ClockRunning,"clock stops safely at zero");
engine.NextPeriod(); Check(engine.State.Period==2&&engine.State.ClockSeconds==720,"period transition restores sport clock preset");
var file=Path.Combine(Path.GetTempPath(),$"scoreboard-{Guid.NewGuid():N}.scoreboard"); engine.Save(file); var other=new GameEngine(); other.Load(file);
Check(other.State.HomeTeam=="Eagles"&&other.State.Period==2&&other.State.Events.Count>0,"portable project saves game, teams, and event history"); File.Delete(file);
var catalogPath=Path.Combine(Path.GetTempPath(),$"sports-{Guid.NewGuid():N}.json");
try
{
    var catalog=new SportCatalog(catalogPath); catalog.Duplicate("basketball","Community Basketball");
    var custom=catalog.Custom.Single(); custom=catalog.AddOrUpdate(custom with { Periods=2, PeriodSeconds=600, Unit="Half", CountUp=true, ScoringOptions=[1,4] });
    var reloaded=new SportCatalog(catalogPath); Check(reloaded.Find(custom.Id)?.ScoringOptions.SequenceEqual([1,4])==true,"custom sport persists configurable scoring and game rules");
    var customGame=new GameEngine(reloaded); customGame.NewGame(custom.Id);
    Check(customGame.State.ClockSeconds==600&&customGame.State.ClockCountsUp&&customGame.State.TotalPeriods==2&&customGame.State.PeriodLabel=="Half","custom sport settings initialize a new game's rules");
    Check(!reloaded.Delete("basketball")&&reloaded.Delete(custom.Id),"built-in presets are protected while custom sports can be deleted");
}
finally { if(File.Exists(catalogPath))File.Delete(catalogPath); }
var templateEngine=new GameEngine();templateEngine.NewGame("football");templateEngine.SetFootballDistance(3);templateEngine.AddFootballYards(true,20);var downBug=ScoreboardTemplates.Bind("{{DOWNS}} / {{YARDS}}",templateEngine.State);Check(downBug.Contains("1st & 3")&&downBug.Contains("20–0"),"scorebug template binding exposes football down and yard stats");
var liveSource=new GameEngine();var liveOutput=new GameEngine();liveSource.NewGame("football");liveOutput.LoadFromState(liveSource.CloneState());liveSource.SetFootballDistance(2,31);liveSource.Score(true,6);liveOutput.UpdateLiveScoreClock(liveSource.State);Check(liveOutput.State.HomeScore==6&&liveOutput.State.YardsToGoal==35,"live output updates scores without publishing operator game-state edits");liveOutput.LoadFromState(liveSource.CloneState());liveSource.SetFootballDistance(1,20);liveOutput.UpdateLiveScoreClock(liveSource.State);Check(liveOutput.State.YardsToGoal==31,"live output retains explicit-publish sport-specific data");
var iconPath=Path.Combine(Path.GetTempPath(),$"icons-{Guid.NewGuid():N}.json");try{var iconCatalog=new ScorebugCatalog(iconPath);iconCatalog.Update(layout=>{layout.Elements.Clear();layout.Elements.Add(new ScorebugElement{Name="Timeouts",Content="",Icon=ScorebugIcon.TimeoutBars});layout.Elements.Add(new ScorebugElement{Name="Ball",Content="",Icon=ScorebugIcon.PossessionDot,IconFlipped=true});});var iconReloaded=new ScorebugCatalog(iconPath);Check(iconReloaded.Layout.Elements[0].Icon==ScorebugIcon.TimeoutBars&&iconReloaded.Layout.Elements[1].Icon==ScorebugIcon.PossessionDot&&iconReloaded.Layout.Elements[1].IconFlipped,"timeout-bar and possession-dot icons persist in the scorebug layout");}finally{if(File.Exists(iconPath))File.Delete(iconPath);}
var layoutPath=Path.Combine(Path.GetTempPath(),$"scorebug-{Guid.NewGuid():N}.json");
try
{
    var catalog=new ScorebugCatalog(layoutPath);Check(catalog.Layout.Elements.Count>=8,"default scorebug has editable team, score, clock and logo elements");
    var logo=new ScorebugElement{Name="Sponsor bug",Kind=ScorebugElementKind.Logo,Content="SPONSOR",Animation=ScorebugAnimation.Pulse,X=1400,Y=-20,Opacity=1.5};
    catalog.Update(layout=>layout.Elements.Add(logo));
    var loaded=new ScorebugCatalog(layoutPath);var saved=loaded.Layout.Elements.Single(x=>x.Name=="Sponsor bug");
    Check(saved.Kind==ScorebugElementKind.Logo&&saved.Animation==ScorebugAnimation.Pulse&&saved.X==1280&&saved.Y==0&&saved.Opacity==1,"scorebug customization persists and clamps unsafe geometry");
    loaded.Update(layout=>layout.Elements.RemoveAll(x=>x.Id==logo.Id));Check(loaded.Layout.Elements.All(x=>x.Id!=logo.Id),"scorebug elements can be removed");
}
finally{if(File.Exists(layoutPath))File.Delete(layoutPath);}
engine.NewGame("football");engine.AdvanceDown();Check(engine.State.Down==2&&engine.State.YardsToGo==10,"football advances from first to second down");engine.AdvanceDown();engine.AdvanceDown();engine.AdvanceDown();Check(engine.State.Down==1,"football fourth down starts a new series");engine.SetFootballDistance(4,22);Check(engine.State.YardsToGo==4&&engine.State.YardsToGoal==22,"football yardage and distance are editable");
engine.NewGame("soccer");engine.AddCard(true);engine.AddCard(false,true);Check(engine.State.HomeYellowCards==1&&engine.State.AwayRedCards==1,"soccer yellow and red cards track per side");Check(engine.Undo()&&engine.State.AwayRedCards==0,"soccer card can be undone");
var sportsGame=Path.Combine(Path.GetTempPath(),$"sports-state-{Guid.NewGuid():N}.scoreboard");engine.NewGame("football");engine.SetFootballDistance(3,17);engine.AdvanceDown();engine.Save(sportsGame);var restored=new GameEngine();restored.Load(sportsGame);Check(restored.State.Down==2&&restored.State.YardsToGo==3&&restored.State.YardsToGoal==17,"sport-specific state persists in saved game");File.Delete(sportsGame);
engine.NewGame("baseball");engine.BaseballBall();engine.BaseballBall();engine.BaseballBall();engine.BaseballBall();Check(engine.State.Balls==0&&engine.State.Strikes==0,"four baseball balls reset the count");
engine.BaseballStrike();engine.BaseballStrike();engine.BaseballStrike(true);Check(engine.State.Strikes==2&&engine.State.Outs==0,"baseball foul at two strikes does not record a third strike");
engine.BaseballStrike();Check(engine.State.Strikes==0&&engine.State.Balls==0&&engine.State.Outs==1,"third strike records an out and resets the count");
engine.BaseballHit(true);Check(engine.State.HomeHits==1&&engine.State.Balls==0&&engine.State.Strikes==0,"baseball hit is credited and resets the count");Check(engine.Undo()&&engine.State.HomeHits==0,"baseball hit supports undo");
var baseballSave=Path.Combine(Path.GetTempPath(),$"baseball-{Guid.NewGuid():N}.scoreboard");engine.BaseballHit(false);engine.Save(baseballSave);var baseballReload=new GameEngine();baseballReload.Load(baseballSave);Check(baseballReload.State.AwayHits==1&&baseballReload.State.Outs==1,"baseball counts and hits persist in game saves");File.Delete(baseballSave);
engine.NewGame("basketball");engine.AddFoul(true);engine.SetShotClock(24);engine.ToggleShotClock();Thread.Sleep(1100);engine.Tick();Check(engine.State.HomeFouls==1&&engine.State.ShotClockSeconds<=23&&engine.State.ShotClockRunning,"basketball tracks fouls and runs an independent shot clock");engine.ResetShotClock();Check(engine.State.ShotClockSeconds==24&&!engine.State.ShotClockRunning,"basketball shot clock resets to 24 and stops");Check(engine.Undo()&&engine.State.ShotClockRunning,"basketball shot clock action can be undone");
engine.NewGame("football");engine.AddFootballYards(true,25);engine.AddTurnover(false);Check(engine.State.HomeYards==25&&engine.State.AwayTurnovers==1,"football tracks team yards and turnovers");Check(engine.Undo()&&engine.State.AwayTurnovers==0,"football turnover supports undo");
engine.NewGame("soccer");engine.AddSoccerShot(true);engine.AddSoccerCorner(false);Check(engine.State.HomeShots==1&&engine.State.AwayCorners==1,"soccer tracks shots and corners by team");
engine.NewGame("soccer");engine.SetTeams("<Home & Co>","Visitors");engine.AddSoccerShot(true);var report=GameReport.Build(engine.State);Check(report.Contains("&lt;Home &amp; Co&gt;")&&report.Contains("Event timeline")&&report.Contains("Shots: 1 — 0"),"game report escapes names and summarizes stats and timeline");
engine.NewGame("football");engine.SetTeams("<Home & Co>","Visitors");engine.SetFootballDistance(2,33);engine.AdvanceDown();engine.AddFootballYards(true,14);engine.AddTurnover(false);var footballReport=GameReport.Build(engine.State);Check(footballReport.Contains("2nd &amp; 2")&&footballReport.Contains("Total yards: &lt;Home &amp; Co&gt; 14")&&footballReport.Contains("Turnovers: 0 — 1"),"football report includes down, yardage, and turnovers");
engine.NewGame("basketball");engine.AddFoul(true);var basketballReport=GameReport.Build(engine.State);Check(basketballReport.Contains("Fouls: 1 — 0")&&basketballReport.Contains("Shot clock: 30s"),"basketball report includes fouls and shot clock");
engine.NewGame("baseball");engine.SetTeams("<Home & Co>","Visitors");engine.BaseballHit(true);var baseballReport=GameReport.Build(engine.State);Check(baseballReport.Contains("Hits: &lt;Home &amp; Co&gt; 1")&&baseballReport.Contains("Count: 0 balls"),"baseball report includes hits and count");
engine.NewGame("football");engine.SetFootballDistance(4,18);engine.PreviousDown();Check(engine.State.Down==1&&engine.State.YardsToGo==4,"previous down steps back from first down safely");engine.AdvanceDown();engine.SetFootballDistance(7,42);engine.NewSeries();Check(engine.State.Down==1&&engine.State.YardsToGo==10,"new series resets the down to first and ten");Check(engine.Undo()&&engine.State.YardsToGo==7,"new series can be undone");
engine.NewGame("football");Check(PeriodDisplay.Format(engine.State)=="Quarter 1","default period label is Quarter 1");engine.SetPeriodFormat("Short");Check(PeriodDisplay.Format(engine.State)=="Q1"&&ScoreboardTemplates.Bind("{{PERIOD}}",engine.State)=="Q1","short period label is Q1");Check(ScoreboardTemplates.Bind("{{PERIOD}}",engine.State,"Full")=="QUARTER 1"&&ScoreboardTemplates.Bind("{{PERIOD}}",engine.State,"Number")=="1","a studio element can choose its own quarter style");engine.SetPeriodFormat("Number");Check(PeriodDisplay.Format(engine.State)=="1","numeric period label is a bare number");var periodReport=GameReport.Build(engine.State);Check(periodReport.Contains("1 of 4"),"game report honors the selected period format");engine.NewGame("soccer");engine.SetPeriodFormat("Short");Check(PeriodDisplay.Format(engine.State)=="Half 1","period format only applies to quarter-based sports");
var shapeLayout=new ScorebugElement{Kind=ScorebugElementKind.Shape,Shape=ScorebugShape.Triangle};var shapeCatalogPath=Path.Combine(Path.GetTempPath(),$"shape-{Guid.NewGuid():N}.json");try{var catalog=new ScorebugCatalog(shapeCatalogPath);catalog.Update(layout=>layout.Elements.Add(shapeLayout));var reloaded=new ScorebugCatalog(shapeCatalogPath);Check(reloaded.Layout.Elements[^1].Shape==ScorebugShape.Triangle,"scorebug shapes persist in the studio layout");}finally{if(File.Exists(shapeCatalogPath))File.Delete(shapeCatalogPath);}
Check(ClockFormat.TryParse("10:30",out var parsedClock)&&parsedClock==630,"MM:SS clock entry parses minutes and seconds");Check(ClockFormat.TryParse("1:05",out var shortClock)&&shortClock==65,"single-digit minutes parse correctly");Check(ClockFormat.TryParse("720",out var rawClock)&&rawClock==720,"plain seconds still parse");Check(!ClockFormat.TryParse("10:75",out _)&&!ClockFormat.TryParse("abc",out _),"invalid clock entry is rejected");Check(ClockFormat.Format(630)=="10:30"&&ClockFormat.Format(0)=="00:00","clock formatting round trips");
engine.NewGame("football");Check(engine.State.HomeTimeouts==3&&engine.State.AwayTimeouts==3,"new games take the timeout count from the sport profile");var timeoutCatalogPath=Path.Combine(Path.GetTempPath(),$"timeouts-{Guid.NewGuid():N}.json");try{var timeouts=new SportCatalog(timeoutCatalogPath);timeouts.SaveProfile(timeouts.Find("football")! with { Timeouts=7 });var reloaded=new SportCatalog(timeoutCatalogPath);Check(reloaded.Find("football")!.Timeouts==7,"editing a built-in profile is saved as an override");var overrideGame=new GameEngine(reloaded);overrideGame.NewGame("football");Check(overrideGame.State.HomeTimeouts==7,"overridden profile timeouts reach a new game");Check(reloaded.ResetOverride("football")&&reloaded.Find("football")!.Timeouts==3,"resetting an override restores the preset");}finally{if(File.Exists(timeoutCatalogPath))File.Delete(timeoutCatalogPath);}
engine.NewGame("soccer");var stat=engine.AddStat("Turnovers");engine.AdjustStat(stat.Id,2);engine.AdjustStat(stat.Id,1);Check(engine.State.Stats.Single().Value==3,"custom stats track a single value");Check(ScoreboardTemplates.Bind("{{STAT:Turnovers}}",engine.State)=="3"&&ScoreboardTemplates.Bind("{{stat:turnovers}}",engine.State)=="3","custom stat tokens display the value");Check(ScoreboardTemplates.Bind("{{STAT:Missing}}",engine.State)=="","missing custom stat tokens render empty");Check(engine.Undo()&&engine.State.Stats.Single().Value==2,"custom stat changes are undoable");var statsReport=GameReport.Build(engine.State);Check(statsReport.Contains("Custom stats")&&statsReport.Contains("Turnovers: 2"),"game report lists custom stats");engine.RemoveStat(stat.Id);Check(engine.State.Stats.Count==0,"custom stats can be removed");
engine.NewGame("football");var exact=engine.AddStat("Penalties");engine.SetStat(exact.Id,4);Check(engine.State.Stats.Single().Value==4,"typed custom stat values set the exact count");engine.SetStat(exact.Id,1);Check(engine.Undo()&&engine.State.Stats.Single().Value==4,"typed custom stat values are undoable");var stagedBefore=GameEngine.StagedSignature(engine.State);engine.Score(true,6);engine.SetClock(42);engine.SetPlayClock(12);Check(GameEngine.StagedSignature(engine.State)==stagedBefore,"score and clocks are published live instead of staged");engine.SetTeams("Home","Away");Check(GameEngine.StagedSignature(engine.State)==stagedBefore,"team name changes publish live like scores");engine.AdvanceDown();Check(GameEngine.StagedSignature(engine.State)!=stagedBefore,"down and distance wait for an explicit output update");
var liveNames=new GameEngine();liveNames.NewGame("football");var nameTarget=new GameEngine();nameTarget.LoadFromState(liveNames.CloneState());liveNames.SetTeams("Home Team","Away Team");nameTarget.UpdateLiveScoreClock(liveNames.State);Check(nameTarget.State.HomeTeam=="Home Team"&&nameTarget.State.HomeAbbreviation=="HT","live publishing carries renamed teams to the output");
var typedNames=new GameEngine();typedNames.NewGame("football");var typedTarget=new GameEngine();typedTarget.LoadFromState(typedNames.CloneState());typedNames.SetTeams("New England","ny giants");Check(ScoreboardTemplates.Bind("{{HOME_TEAM}} vs {{AWAY_TEAM}}",typedNames.State)=="New England vs ny giants","scorebug renders the full team name in the case the operator typed");Check(ScoreboardTemplates.Bind("{{HOME_ABBR}}/{{AWAY_ABBR}}",typedNames.State)=="NE/NG","abbreviation tokens still render initials for compact labels");typedTarget.UpdateLiveScoreClock(typedNames.State);Check(ScoreboardTemplates.Bind("{{HOME_TEAM}} vs {{AWAY_TEAM}}",typedTarget.State)=="New England vs ny giants","live publishing carries the typed team name to the on-air graphic");
engine.NewGame("football");engine.SetFootballDistance(3,74);Check(engine.State.YardsToGoal==50,"the ball spot stops at the 50-yard line");engine.SetPossession(false);Check(engine.State.YardsToGoal==50&&ScoreboardTemplates.Bind("{{BALL_ON}}",engine.State)=="50","the ball spot is not tied to possession");engine.SetFootballDistance(3,-6);Check(engine.State.YardsToGoal==0,"the ball spot stops at the goal line");
engine.NewGame("football");engine.SetTeams("Home Team","Away Team");engine.SetFootballDistance(2,37);Check(engine.State.BallOnHomeSide&&ScoreboardTemplates.Bind("{{BALL_SIDE}}",engine.State)=="HT","the ball spot starts on the home half of the field");engine.SetPossession(false);Check(ScoreboardTemplates.Bind("{{BALL_SIDE}}",engine.State)=="HT"&&engine.State.YardsToGoal==37,"possession does not decide which half the ball is on");var beforeSide=GameEngine.StagedSignature(engine.State);engine.SetBallSide(false);Check(ScoreboardTemplates.Bind("{{BALL_SIDE}}",engine.State)=="AT"&&ScoreboardTemplates.Bind("{{BALL_SIDE_SPOT}}",engine.State)=="AT 37","the ball spot can sit on the visitors' half");Check(GameEngine.StagedSignature(engine.State)!=beforeSide,"a ball-side change waits for Update Output");Check(engine.Undo()&&engine.State.BallOnHomeSide,"moving the ball to the other half can be undone");
var sameCatalogPath=Path.Combine(Path.GetTempPath(),$"same-{Guid.NewGuid():N}.json");try{var liveCatalog=new SportCatalog(sameCatalogPath);var liveEngine=new GameEngine(liveCatalog);liveEngine.NewGame("football");liveCatalog.SaveProfile(liveCatalog.Find("football")! with { Timeouts=4, ScoringOptions=[1,2,3,6] });liveEngine.NewGame("football");Check(liveEngine.State.HomeTimeouts==4&&liveEngine.State.ScoringOptions.Contains(6),"a sport profile edited while the app runs reaches the next new game without a relaunch");}finally{if(File.Exists(sameCatalogPath))File.Delete(sameCatalogPath);}
var autosaveFolder=Path.Combine(Path.GetTempPath(),$"autosaves-{Guid.NewGuid():N}");AutosaveStore.UseFolder(autosaveFolder);try{var auto=new GameEngine();auto.NewGame("football");auto.SetTeams("Home Squad","Away Squad");auto.Score(true,7);AutosaveStore.Save(auto);AutosaveStore.Save(auto);var points=AutosaveStore.List();Check(points.Count==1&&points[0].HomeScore==7&&points[0].HomeTeam=="Home Squad","autosaves keep one recoverable point inside the same minute");Check(points[0].Path.EndsWith(".spg",StringComparison.OrdinalIgnoreCase),"autosaves are stored as .spg game files");auto.NewGame("football");var afterNewGame=AutosaveStore.Save(auto,startNewPoint:true);Check(afterNewGame.Count==2&&afterNewGame[0].HomeScore==0&&afterNewGame[1].HomeScore==7,"a new game starts its own recoverable point and leaves the previous game recoverable");for(var i=0;i<AutosaveStore.Keep+3;i++)AutosaveStore.Save(auto,startNewPoint:true);Check(AutosaveStore.List().Count==AutosaveStore.Keep,$"autosaves are pruned to the newest {AutosaveStore.Keep} points");}finally{AutosaveStore.UseFolder(null);try{Directory.Delete(autosaveFolder,true);}catch{}}
var liveTick=new GameEngine();liveTick.NewGame("basketball");var tickNotified=0;liveTick.Changed+=()=>tickNotified++;Thread.Sleep(1100);liveTick.Tick();Check(tickNotified==0,"idle ticks do not notify the UI when no clock is running");

// ---------- team identity: name, abbreviation, logo and palette ----------
var identityEngine=new GameEngine();identityEngine.NewGame("football");
identityEngine.SetTeamIdentity(false,"Rovers FC","RFC",@"C:\logos\rovers.png","#FFAA1122","#FF223344");
Check(identityEngine.State.AwayTeam=="Rovers FC"&&identityEngine.State.AwayAbbreviation=="RFC"&&identityEngine.State.AwayLogoPath!.EndsWith("rovers.png",StringComparison.OrdinalIgnoreCase)&&identityEngine.State.ColorFor(ScorebugColorSource.AwayPrimary)=="#FFAA1122","team details save the name, abbreviation, logo and colours");
var identityTarget=new GameEngine();identityTarget.LoadFromState(identityEngine.CloneState());
identityEngine.SetTeamIdentity(true,"Home Town","HT",null,"#FF010203","#FF040506");
identityTarget.UpdateLiveScoreClock(identityEngine.State);
Check(identityTarget.State.HomePrimaryColor=="#FF010203"&&identityTarget.State.AwayTeam=="Rovers FC"&&identityTarget.State.AwayLogoPath is not null,"team details publish live like the score and team names");
identityEngine.SetTeamIdentity(true,"Home Town","HT",null,"not-a-colour","#FF040506");
Check(identityEngine.State.HomePrimaryColor.StartsWith('#')&&identityEngine.State.HomePrimaryColor.Length is 7 or 9,"an invalid team colour never reaches the graphic");
Check(identityEngine.State.ColorFor(ScorebugColorSource.Custom) is null&&identityEngine.State.LogoFor(ScorebugLogoSide.None) is null,"a custom colour and no logo resolve to the element's own values");

// ---------- layouts: several scorebugs in one game, one on air at a time ----------
var layoutsPath=Path.Combine(Path.GetTempPath(),$"layouts-{Guid.NewGuid():N}.json");
try
{
    var layouts=new ScorebugCatalog(layoutsPath);
    Check(layouts.Layouts.Count==1&&layouts.Layout.Elements.Count>=8,"a fresh catalog opens with the default layout");
    var second=layouts.AddLayout("Slideshow");
    layouts.Update(layout=>{layout.Elements.Clear();layout.Elements.Add(new ScorebugElement{Name="Slide",Content="SECOND LAYOUT",X=20,Y=30,Width=600,Height=90});});
    Check(layouts.Layout.Name=="Slideshow"&&layouts.Layout.Elements.Count==1,"a new layout holds its own artwork");
    layouts.PublishLayout();
    layouts.SelectLayout(0);
    Check(layouts.ActiveIndex==0&&layouts.PublishedIndex==second,"switching the preview leaves the on-air layout alone until it is published");
    var reloadedLayouts=new ScorebugCatalog(layoutsPath);
    Check(reloadedLayouts.Layouts.Count==2&&reloadedLayouts.Layouts[1].Name=="Slideshow"&&reloadedLayouts.PublishedIndex==1,"layouts and the published choice persist in scorebug.json");
    var copy=reloadedLayouts.AddLayout("Copy",copyActive:true);
    Check(reloadedLayouts.Layouts[copy].Elements[0].Id!=reloadedLayouts.Layouts[1].Elements[0].Id,"a duplicated layout edits independently of the original");
    Check(reloadedLayouts.RemoveLayout(copy)&&reloadedLayouts.Layouts.Count==2,"a layout can be deleted");
}
finally{if(File.Exists(layoutsPath))File.Delete(layoutsPath);}
var lastLayoutPath=Path.Combine(Path.GetTempPath(),$"last-layout-{Guid.NewGuid():N}.json");
try{var single=new ScorebugCatalog(lastLayoutPath);Check(!single.RemoveLayout(0),"the last layout cannot be deleted");}finally{if(File.Exists(lastLayoutPath))File.Delete(lastLayoutPath);}
var legacyLayoutPath=Path.Combine(Path.GetTempPath(),$"legacy-layout-{Guid.NewGuid():N}.json");
try
{
    File.WriteAllText(legacyLayoutPath,JsonSerializer.Serialize(ScorebugLayout.Default()));
    var migrated=new ScorebugCatalog(legacyLayoutPath);
    Check(migrated.Layouts.Count==1&&migrated.Layouts[0].Elements.Count>=8,"an older single-layout file becomes the first layout");
}
finally{if(File.Exists(legacyLayoutPath))File.Delete(legacyLayoutPath);}

// ---------- typography, text transitions and team colour slots ----------
var styledPath=Path.Combine(Path.GetTempPath(),$"styled-{Guid.NewGuid():N}.json");
try
{
    var styledCatalog=new ScorebugCatalog(styledPath);
    styledCatalog.Update(layout=>
    {
        layout.Elements.Clear();
        layout.Elements.Add(new ScorebugElement
        {
            Name="Styled",FontFamily="Georgia",Bold=false,Italic=true,Underline=true,StrikeThrough=true,
            Transition=ScorebugTransition.WheelUp,ForegroundSource=ScorebugColorSource.AwayPrimary,BackgroundSource=ScorebugColorSource.HomeSecondary
        });
    });
    var styledReloaded=new ScorebugCatalog(styledPath);
    var savedStyle=styledReloaded.Layout.Elements.Single();
    Check(savedStyle.FontFamily=="Georgia"&&!savedStyle.Bold&&savedStyle.Italic&&savedStyle.Underline&&savedStyle.StrikeThrough,"font family, bold, italic, underline and strike persist in the layout");
    Check(savedStyle.Transition==ScorebugTransition.WheelUp&&savedStyle.ForegroundSource==ScorebugColorSource.AwayPrimary&&savedStyle.BackgroundSource==ScorebugColorSource.HomeSecondary,"the text transition and team colour slots persist in the layout");
}
finally{if(File.Exists(styledPath))File.Delete(styledPath);}
Check(Enum.GetValues<ScorebugTransition>().Length>=10&&Enum.GetValues<ScorebugColorSource>().Length==5,"there are at least ten value animations and five colour sources");
