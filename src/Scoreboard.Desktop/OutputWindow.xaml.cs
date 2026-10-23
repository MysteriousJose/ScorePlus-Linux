using System.Windows;
using System.Windows.Controls;
using Scoreboard.Core;
namespace Scoreboard.Desktop;
public partial class OutputWindow : Window
{
    readonly GameEngine _engine;
    readonly ScorebugCanvasView _scorebugView;
    WindowStyle _style; WindowState _state; bool _full;
    public OutputWindow(GameEngine engine, ScorebugCatalog catalog) { InitializeComponent(); _engine = engine; _scorebugView=new ScorebugCanvasView(catalog,engine); ScorebugHost.Children.Add(_scorebugView); _engine.Changed += Update; Update(); }
    void Update()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Update); return; }
        var s = _engine.State;
        HomeLabel.Text = s.HomeAbbreviation; AwayLabel.Text = s.AwayAbbreviation;
        HomeScore.Text = s.HomeScore.ToString(); AwayScore.Text = s.AwayScore.ToString();
        Period.Text = $"{s.PeriodLabel.ToUpperInvariant()} {s.Period}"; Clock.Text = GameEngine.FormatClock(s.ClockSeconds);
        HomeTimeouts.Text = Dots(s.HomeTimeouts); AwayTimeouts.Text = Dots(s.AwayTimeouts);
        Possession.Visibility = _engine.Sport.Id is "basketball" or "football" ? Visibility.Visible : Visibility.Collapsed;
        Possession.Text = $"●  {(s.HomePossession ? s.HomeAbbreviation : s.AwayAbbreviation)} POSSESSION";
        if(s.SportId=="football"){Period.Text=$"{s.PeriodLabel.ToUpperInvariant()} {s.Period} · {s.Down}{OrdinalSuffix(s.Down)} & {s.YardsToGo}";}
        if(s.SportId=="soccer"){HomeTimeouts.Text=$"🟨 {s.HomeYellowCards}   🟥 {s.HomeRedCards}";AwayTimeouts.Text=$"🟨 {s.AwayYellowCards}   🟥 {s.AwayRedCards}";}
    }
    static string Dots(int count) => string.Join(" ", Enumerable.Repeat("●", count));
    static string OrdinalSuffix(int value)=>value switch{1=>"st",2=>"nd",3=>"rd",_=>"th"};
    public void ToggleFullscreen()
    {
        _full = !_full; if (_full) { _style = WindowStyle; _state = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; Topmost = true; }
        else { Topmost = false; WindowStyle = _style; WindowState = _state; }
    }
    protected override void OnClosed(EventArgs e) { _engine.Changed -= Update; _scorebugView.Dispose(); base.OnClosed(e); }
}
