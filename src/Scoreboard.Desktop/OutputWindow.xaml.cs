using System.Windows;
using System.Windows.Controls;
using Scoreboard.Core;
namespace Scoreboard.Desktop;
public partial class OutputWindow : Window
{
    readonly GameEngine _engine;
    readonly ScorebugCatalog _catalog;
    GameState _displayState;
    readonly ScorebugCanvasView _scorebugView;
    string _broadcastTheme;
    WindowStyle _style; WindowState _state; bool _full;
    public OutputWindow(GameEngine engine, ScorebugCatalog catalog,string broadcastTheme="Dark") {InitializeComponent(); _broadcastTheme=broadcastTheme; _engine = engine; _catalog=catalog; _displayState=engine.CloneState(); _scorebugView=new ScorebugCanvasView(catalog,engine,()=>_displayState); ScorebugHost.Children.Add(_scorebugView); _engine.Changed+=Update; _engine.Changed+=_scorebugView.Refresh; _catalog.Changed+=Publish; Update(); }
    public void Publish(){_displayState=_engine.CloneState();Update();_scorebugView.Refresh();}
    public void UpdateLiveScoreClock(GameState source)
    {
        _displayState.HomeScore=source.HomeScore;_displayState.AwayScore=source.AwayScore;
        _displayState.ClockSeconds=source.ClockSeconds;_displayState.ClockRunning=source.ClockRunning;_displayState.ClockCountsUp=source.ClockCountsUp;
        _displayState.ShotClockSeconds=source.ShotClockSeconds;_displayState.ShotClockRunning=source.ShotClockRunning;
        Update();_scorebugView.Refresh();
    }
    public void RefreshDisplay(){Update();_scorebugView.Refresh();}
    public void SetBroadcastTheme(string theme){_broadcastTheme=theme;ThemeManager.Apply(this,theme);}
    void Window_Loaded(object sender,RoutedEventArgs e){ThemeManager.Apply(this,_broadcastTheme);Update();_scorebugView.Refresh();}
    void Update()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Update); return; }
        var s = _displayState;
        Background=ColorBrush(_broadcastTheme=="Light"?"#FFF3F5F8":"#FF000000");
        HomeLabel.Text = s.HomeAbbreviation; AwayLabel.Text = s.AwayAbbreviation;
        HomeScore.Text = s.HomeScore.ToString(); AwayScore.Text = s.AwayScore.ToString();
        Period.Text = $"{s.PeriodLabel.ToUpperInvariant()} {s.Period}"; Clock.Text = GameEngine.FormatClock(s.ClockSeconds);
        HomeExtraLabel.Text = AwayExtraLabel.Text = "TIMEOUTS";
        HomeExtraPanel.Visibility = AwayExtraPanel.Visibility = Visibility.Visible;
        HomeTimeouts.Text = Dots(s.HomeTimeouts); AwayTimeouts.Text = Dots(s.AwayTimeouts);
        Possession.Visibility = s.SportId is "basketball" or "football" ? Visibility.Visible : Visibility.Collapsed;
        Possession.Text = $"●  {(s.HomePossession ? s.HomeAbbreviation : s.AwayAbbreviation)} POSSESSION";
        if(s.SportId=="football"){Period.Text=$"{s.PeriodLabel.ToUpperInvariant()} {s.Period} · {s.Down}{OrdinalSuffix(s.Down)} & {s.YardsToGo}";}
        if(s.SportId=="soccer")
        {
            HomeExtraLabel.Text = AwayExtraLabel.Text = "CARDS · SHOTS · CORNERS";
            HomeTimeouts.Text=$"🟨 {s.HomeYellowCards}   🟥 {s.HomeRedCards}   ⚽ {s.HomeShots}   ◆ {s.HomeCorners}";
            AwayTimeouts.Text=$"🟨 {s.AwayYellowCards}   🟥 {s.AwayRedCards}   ⚽ {s.AwayShots}   ◆ {s.AwayCorners}";
            Possession.Visibility=Visibility.Collapsed;
        }
        else if(s.SportId=="basketball")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="FOULS";HomeTimeouts.Text=$"{s.HomeFouls} fouls";AwayTimeouts.Text=$"{s.AwayFouls} fouls";
            Possession.Text=$"●  {(s.HomePossession?s.HomeAbbreviation:s.AwayAbbreviation)} POSSESSION   ·   SHOT CLOCK {s.ShotClockSeconds}";
        }
        else if(s.SportId=="baseball")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="HITS · COUNT";HomeTimeouts.Text=$"{s.HomeHits} hits   ·   {s.Balls} balls / {s.Strikes} strikes";AwayTimeouts.Text=$"{s.AwayHits} hits   ·   {s.Outs} outs";Possession.Visibility=Visibility.Collapsed;
        }
        else if(s.SportId=="football")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="YARDS · TURNOVERS";HomeTimeouts.Text=$"{s.HomeYards} yards   ·   {s.HomeTurnovers} turnovers";AwayTimeouts.Text=$"{s.AwayYards} yards   ·   {s.AwayTurnovers} turnovers";
        }
    }
    static System.Windows.Media.Brush ColorBrush(string value)=>(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(value)!;
    static string Dots(int count) => string.Join(" ", Enumerable.Repeat("●", count));
    static string OrdinalSuffix(int value)=>value switch{1=>"st",2=>"nd",3=>"rd",_=>"th"};
    public void ToggleFullscreen()
    {
        _full = !_full; if (_full) { _style = WindowStyle; _state = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; Topmost = true; }
        else { Topmost = false; WindowStyle = _style; WindowState = _state; }
    }
    protected override void OnClosed(EventArgs e) { _engine.Changed-=Update; _engine.Changed-=_scorebugView.Refresh; _catalog.Changed-=Publish; _scorebugView.Dispose(); base.OnClosed(e); }
}
