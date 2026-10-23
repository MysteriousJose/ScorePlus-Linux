using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;
public partial class MainWindow : Window
{
    readonly GameEngine _engine = new();
    readonly ScorebugCatalog _scorebug = new();
    ScorebugCanvasView? _preview;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(5) };
    OutputWindow? _output;
    GameControlWindow? _controlPanel;
    readonly UserPreferences _preferences = PreferencesStore.Load();
    readonly string _recoveryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scoreboard", "recovery.scoreboard");
    string? _projectPath;
    bool _initializing = true;
    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Apply(this,_preferences.Theme);
        SportSelector.DisplayMemberPath = "Name"; SportSelector.SelectedValuePath = "Id"; RefreshSports(); SportSelector.SelectedValue = _engine.State.SportId;
        _engine.Changed += Refresh; _timer.Tick += (_, _) => _engine.Tick(); _timer.Start();
        _autosave.Tick += (_, _) => AutoSave(); _autosave.Start();
        _preview = new ScorebugCanvasView(_scorebug,_engine);
        PreviewHost.Content = _preview;
        Refresh(); _initializing = false;
        if (_preferences.SeparateControls) ControlPanel_Click(this,new RoutedEventArgs());
        if (File.Exists(_recoveryPath) && MessageBox.Show("A recovered game is available. Restore it?", "Game recovery", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        { try { _engine.Load(_recoveryPath); StatusText.Text = "RECOVERED · autosave restored"; } catch { } }
    }
    UIElement CreateScoreboardVisual()
    {
        var grid = new Grid { Background = new SolidColorBrush(Color.FromRgb(9,16,24)), Margin = new Thickness(0), MinHeight = 280 };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        StackPanel Team(bool home)
        {
            var box = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var name = new TextBlock { Name = home ? "PreviewHomeName" : "PreviewAwayName", Text = home ? "HOME" : "AWAY", FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var score = new TextBlock { Name = home ? "PreviewHomeScore" : "PreviewAwayScore", Text = "0", FontSize = 100, FontWeight = FontWeights.Bold, Foreground = home ? new SolidColorBrush(Color.FromRgb(56,215,163)) : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            box.Children.Add(name); box.Children.Add(score); return box;
        }
        var h = Team(true); Grid.SetColumn(h,0); grid.Children.Add(h); var a = Team(false); Grid.SetColumn(a,2); grid.Children.Add(a);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        mid.Children.Add(new TextBlock { Name = "PreviewPeriod", Text = "PERIOD 1", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.LightGray });
        mid.Children.Add(new TextBlock { Name = "PreviewClock", Text = "12:00", FontSize = 54, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0,20,0,0) }); Grid.SetColumn(mid,1); grid.Children.Add(mid);
        return grid;
    }
    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var s = _engine.State;
        if (!HomeName.IsKeyboardFocusWithin) HomeName.Text = s.HomeTeam;
        if (!AwayName.IsKeyboardFocusWithin) AwayName.Text = s.AwayTeam;
        HomeScore.Text = s.HomeScore.ToString(); AwayScore.Text = s.AwayScore.ToString();
        ClockText.Text = GameEngine.FormatClock(s.ClockSeconds); PeriodText.Text = $"{s.PeriodLabel.ToUpperInvariant()} {s.Period} / {s.TotalPeriods}";
        ClockButton.Content = s.ClockRunning ? "Ⅱ  STOP" : "▶  START";
        FootballPanel.Visibility=s.SportId=="football"?Visibility.Visible:Visibility.Collapsed;SoccerPanel.Visibility=s.SportId=="soccer"?Visibility.Visible:Visibility.Collapsed;
        DownDisplay.Text=$"{s.Down}{(s.Down==1?"st":s.Down==2?"nd":s.Down==3?"rd":"th")} & {s.YardsToGo}";BallDisplay.Text=$"Ball at {s.YardsToGoal} yd line";
        CardsDisplay.Text=$"Home  🟨 {s.HomeYellowCards}   🟥 {s.HomeRedCards}     Away  🟨 {s.AwayYellowCards}   🟥 {s.AwayRedCards}";
        EventList.ItemsSource = s.Events.AsEnumerable().Reverse().Take(100).Select(x => $"{x.At:HH:mm:ss}  {x.Type,-10} {x.Description}").ToArray();
    }
    static T? Find<T>(DependencyObject parent, string name) where T : FrameworkElement
    { for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i); if(child is T item && item.Name==name)return item; var nested=Find<T>(child,name); if(nested!=null)return nested; } return null; }
    void RefreshSports() { SportSelector.ItemsSource = _engine.Sports.All; }
    void ScorebugStudio_Click(object sender,RoutedEventArgs e) { var studio=new ScorebugEditorWindow(_scorebug,_engine){Owner=this}; studio.ShowDialog(); }
    void Preferences_Click(object sender,RoutedEventArgs e){var dialog=new SettingsWindow(_preferences){Owner=this};dialog.ShowDialog();ThemeManager.Apply(this,_preferences.Theme);if(_controlPanel is {IsVisible:true})ThemeManager.Apply(_controlPanel,_preferences.Theme);if(_output is {IsVisible:true})ThemeManager.Apply(_output,_preferences.Theme);}
    void ControlPanel_Click(object sender,RoutedEventArgs e){if(_controlPanel is {IsVisible:true}){_controlPanel.Activate();return;}_controlPanel=new GameControlWindow(_engine);ThemeManager.Apply(_controlPanel,_preferences.Theme);_controlPanel.Show();}
    void SportProfiles_Click(object sender,RoutedEventArgs e) { var dialog=new SportProfileWindow(_engine.Sports){Owner=this}; dialog.ShowDialog(); var activeId=SportSelector.SelectedValue as string; RefreshSports(); if(_engine.Sports.Find(activeId) is not null)SportSelector.SelectedValue=activeId; }
    void Sport_Changed(object sender, SelectionChangedEventArgs e) { if (_initializing || SportSelector.SelectedValue is not string id) return; _engine.NewGame(id); StatusText.Text = $"NEW {_engine.Sport.Name.ToUpperInvariant()} GAME"; }
    void NewGame_Click(object sender,RoutedEventArgs e) { if(MessageBox.Show("Start a new game? This resets scores and the game clock.","New game",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return; _engine.NewGame(SportSelector.SelectedValue as string ?? "basketball"); StatusText.Text="NEW GAME"; }
    void HomeScore_Click(object sender,RoutedEventArgs e) => _engine.Score(true,int.Parse((string)((Button)sender).Tag));
    void AwayScore_Click(object sender,RoutedEventArgs e) => _engine.Score(false,int.Parse((string)((Button)sender).Tag));
    void ScoreEdit_Click(object sender,MouseButtonEventArgs e) { var home=sender==HomeScore; var dialog=new NumberDialog(home?"Set home score":"Set away score", home?_engine.State.HomeScore:_engine.State.AwayScore) { Owner=this }; if(dialog.ShowDialog()==true)_engine.SetScore(home,dialog.Value); }
    void Teams_Click(object sender,RoutedEventArgs e) => _engine.SetTeams(HomeName.Text.Trim(),AwayName.Text.Trim());
    void Clock_Click(object sender,RoutedEventArgs e) => _engine.ToggleClock();
    void MinusMinute_Click(object sender,RoutedEventArgs e) => _engine.AdjustClock(-60);
    void PlusMinute_Click(object sender,RoutedEventArgs e) => _engine.AdjustClock(60);
    void MinusSecond_Click(object sender,RoutedEventArgs e) => _engine.AdjustClock(-1);
    void PlusSecond_Click(object sender,RoutedEventArgs e) => _engine.AdjustClock(1);
    void SetClock_Click(object sender,RoutedEventArgs e) { var d=new NumberDialog("Set clock (seconds)",_engine.State.ClockSeconds){Owner=this}; if(d.ShowDialog()==true)_engine.SetClock(d.Value); }
    void NextPeriod_Click(object sender,RoutedEventArgs e) => _engine.NextPeriod();
    void Possession_Click(object sender,RoutedEventArgs e) => _engine.TogglePossession();
    void HomeTimeout_Click(object sender,RoutedEventArgs e) => _engine.Timeout(true);
    void NextDown_Click(object sender,RoutedEventArgs e){if(_engine.State.Down==4&&MessageBox.Show(this,"Fourth down. Start a new series and reset distance?","New series",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;_engine.AdvanceDown();}
    void DistanceMinus_Click(object sender,RoutedEventArgs e)=>_engine.SetFootballDistance(_engine.State.YardsToGo-1);
    void DistancePlus_Click(object sender,RoutedEventArgs e)=>_engine.SetFootballDistance(_engine.State.YardsToGo+1);
    void BallMinus_Click(object sender,RoutedEventArgs e)=>_engine.SetFootballDistance(_engine.State.YardsToGo,Math.Min(99,_engine.State.YardsToGoal+5));
    void BallPlus_Click(object sender,RoutedEventArgs e)=>_engine.SetFootballDistance(_engine.State.YardsToGo,Math.Max(0,_engine.State.YardsToGoal-5));
    void NewSeries_Click(object sender,RoutedEventArgs e)=>_engine.SetFootballDistance(10);
    void HomeYellow_Click(object sender,RoutedEventArgs e)=>_engine.AddCard(true);
    void AwayYellow_Click(object sender,RoutedEventArgs e)=>_engine.AddCard(false);
    void HomeRed_Click(object sender,RoutedEventArgs e)=>_engine.AddCard(true,true);
    void AwayRed_Click(object sender,RoutedEventArgs e)=>_engine.AddCard(false,true);
    void AwayTimeout_Click(object sender,RoutedEventArgs e) => _engine.Timeout(false);
    void Undo_Click(object sender,RoutedEventArgs e) { if(!_engine.Undo())StatusText.Text="NOTHING TO UNDO"; }
    void Output_Click(object sender,RoutedEventArgs e) { if(_output is { IsVisible:true }) { _output.Activate(); return; } _output=new OutputWindow(_engine,_scorebug); _output.Show(); }
    void Fullscreen_Click(object sender,RoutedEventArgs e) { Output_Click(sender,e); _output?.ToggleFullscreen(); }
    void Save_Click(object sender,RoutedEventArgs e) { var d=new SaveFileDialog{Filter="Scoreboard Game (*.scoreboard)|*.scoreboard|JSON (*.json)|*.json",DefaultExt="scoreboard",FileName=$"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}"}; if(d.ShowDialog()==true){_engine.Save(d.FileName);_projectPath=d.FileName;StatusText.Text="GAME SAVED";} }
    void Open_Click(object sender,RoutedEventArgs e) { var d=new OpenFileDialog{Filter="Scoreboard Game (*.scoreboard;*.json)|*.scoreboard;*.json|All files (*.*)|*.*"}; if(d.ShowDialog()==true){try{_engine.Load(d.FileName);_projectPath=d.FileName;RefreshSports();SportSelector.SelectedValue=_engine.State.SportId;StatusText.Text="GAME LOADED";}catch(Exception ex){MessageBox.Show($"Could not load game: {ex.Message}");}} }
    void AutoSave() { try { _engine.Save(_recoveryPath); AutosaveText.Text=$"AUTOSAVED {DateTime.Now:HH:mm:ss}"; } catch(Exception ex){StatusText.Text=$"AUTOSAVE WARNING · {ex.Message}";} }
    void Window_KeyDown(object sender,KeyEventArgs e) { if(Keyboard.FocusedElement is TextBox)return; if(e.Key==Key.Space){_engine.ToggleClock();e.Handled=true;} else if(e.Key==Key.Z&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){_engine.Undo();e.Handled=true;} else if(e.Key==Key.Left){_engine.Score(true,1);e.Handled=true;} else if(e.Key==Key.Right){_engine.Score(false,1);e.Handled=true;} }
    protected override void OnClosed(EventArgs e) { _preview?.Dispose(); _controlPanel?.Close(); _output?.Close(); base.OnClosed(e); }
}

sealed class NumberDialog : Window
{
    readonly TextBox _input = new(); public int Value => int.TryParse(_input.Text,out var value)?Math.Max(0,value):0;
    public NumberDialog(string title,int value)
    {
        Title=title; Width=330; Height=170; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterOwner; Background=new SolidColorBrush(Color.FromRgb(23,28,37)); Foreground=Brushes.White;
        var panel=new StackPanel{Margin=new Thickness(15)}; _input.Text=value.ToString(); _input.SelectAll(); panel.Children.Add(_input); var ok=new Button{Content="Apply",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right}; ok.Click+=(_,_)=>{DialogResult=true;Close();}; panel.Children.Add(ok); Content=panel; Loaded+=(_,_)=>_input.Focus();
    }
}
