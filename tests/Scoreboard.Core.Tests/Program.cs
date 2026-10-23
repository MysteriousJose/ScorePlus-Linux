using Scoreboard.Core;
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
