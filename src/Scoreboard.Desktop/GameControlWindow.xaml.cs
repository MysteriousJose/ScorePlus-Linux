using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Scoreboard.Core;
namespace Scoreboard.Desktop;
public partial class GameControlWindow : Window
{
    readonly GameEngine _engine;
    public GameControlWindow(GameEngine engine){InitializeComponent();_engine=engine;_engine.Changed+=Refresh;Refresh();}
    void Refresh(){if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Refresh);return;}var s=_engine.State;HomeTitle.Text=s.HomeTeam;AwayTitle.Text=s.AwayTeam;HomeScore.Text=s.HomeScore.ToString();AwayScore.Text=s.AwayScore.ToString();HomeFouls.Text=$"Fouls: {s.HomeFouls} · Timeouts: {s.HomeTimeouts}";AwayFouls.Text=$"Fouls: {s.AwayFouls} · Timeouts: {s.AwayTimeouts}";Clock.Text=GameEngine.FormatClock(s.ClockSeconds);ClockToggle.Content=s.ClockRunning?"Stop clock":"Start clock";PeriodLabel.Text=$"{s.PeriodLabel} {s.Period} / {s.TotalPeriods}";FootballPanel.Visibility=s.SportId=="football"?Visibility.Visible:Visibility.Collapsed;SoccerPanel.Visibility=s.SportId=="soccer"?Visibility.Visible:Visibility.Collapsed;DownDisplay.Text=$"{Ordinal(s.Down)} & {s.YardsToGo}";BallDisplay.Text=$"Ball at {_engine.State.YardsToGoal} yd line";CardsDisplay.Text=$"Home  🟨 {s.HomeYellowCards}   🟥 {s.HomeRedCards}       Away  🟨 {s.AwayYellowCards}   🟥 {s.AwayRedCards}";}
    static string Ordinal(int n)=>n switch{1=>"1st",2=>"2nd",3=>"3rd",_=>$"{n}th"};
    void HomeScore_Click(object s,RoutedEventArgs e)=>_engine.Score(true,int.Parse((string)((Button)s).Tag));void AwayScore_Click(object s,RoutedEventArgs e)=>_engine.Score(false,int.Parse((string)((Button)s).Tag));
    void HomeTimeout_Click(object s,RoutedEventArgs e)=>_engine.Timeout(true);void AwayTimeout_Click(object s,RoutedEventArgs e)=>_engine.Timeout(false);void Possession_Click(object s,RoutedEventArgs e)=>_engine.TogglePossession();void Clock_Click(object s,RoutedEventArgs e)=>_engine.ToggleClock();void Minus_Click(object s,RoutedEventArgs e)=>_engine.AdjustClock(-60);void Plus_Click(object s,RoutedEventArgs e)=>_engine.AdjustClock(60);void MinusSecond_Click(object s,RoutedEventArgs e)=>_engine.AdjustClock(-1);void PlusSecond_Click(object s,RoutedEventArgs e)=>_engine.AdjustClock(1);void NextPeriod_Click(object s,RoutedEventArgs e)=>_engine.NextPeriod();void Undo_Click(object s,RoutedEventArgs e)=>_engine.Undo();
    void SetClock_Click(object s,RoutedEventArgs e){var d=new ControlNumberDialog("Set clock in seconds",_engine.State.ClockSeconds){Owner=this};if(d.ShowDialog()==true)_engine.SetClock(d.Value);}
    void NextDown_Click(object s,RoutedEventArgs e){if(_engine.State.Down==4&&MessageBox.Show(this,"Fourth down: start a new series and reset distance?","New series",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;_engine.AdvanceDown();}void DistanceMinus_Click(object s,RoutedEventArgs e)=>_engine.SetFootballDistance(_engine.State.YardsToGo-1);void DistancePlus_Click(object s,RoutedEventArgs e)=>_engine.SetFootballDistance(_engine.State.YardsToGo+1);void BallMinus_Click(object s,RoutedEventArgs e)=>_engine.SetFootballDistance(_engine.State.YardsToGo,Math.Min(99,_engine.State.YardsToGoal+5));void BallPlus_Click(object s,RoutedEventArgs e)=>_engine.SetFootballDistance(_engine.State.YardsToGo,Math.Max(0,_engine.State.YardsToGoal-5));void NewSeries_Click(object s,RoutedEventArgs e)=>_engine.SetFootballDistance(10);
    void HomeYellow_Click(object s,RoutedEventArgs e)=>_engine.AddCard(true);void AwayYellow_Click(object s,RoutedEventArgs e)=>_engine.AddCard(false);void HomeRed_Click(object s,RoutedEventArgs e)=>_engine.AddCard(true,true);void AwayRed_Click(object s,RoutedEventArgs e)=>_engine.AddCard(false,true);
    protected override void OnClosed(EventArgs e){_engine.Changed-=Refresh;base.OnClosed(e);}
}
internal sealed class ControlNumberDialog : Window
{
 readonly TextBox _input=new();public int Value=>int.TryParse(_input.Text,out var v)?Math.Max(0,v):0;
 public ControlNumberDialog(string title,int value){Title=title;Width=330;Height=170;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23,28,37));Foreground=System.Windows.Media.Brushes.White;var p=new StackPanel{Margin=new Thickness(15)};_input.Text=value.ToString();p.Children.Add(_input);var ok=new Button{Content="Apply",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right};ok.Click+=(_,_)=>{DialogResult=true;Close();};p.Children.Add(ok);Content=p;Loaded+=(_,_)=>_input.Focus();}
}
