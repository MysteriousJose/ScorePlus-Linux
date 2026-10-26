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
    readonly GameEngine _publishedEngine = new();
    readonly ScorebugCatalog _scorebug = new();
    ScorebugCanvasView? _preview;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(5) };
    OutputWindow? _output;
    readonly List<GameControlWindow> _controlPanels = [];
    int _eventCount = -1;
    readonly UserPreferences _preferences = PreferencesStore.Load();
    readonly string _recoveryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scoreboard", "recovery.scoreboard");
    string? _projectPath;
    bool _initializing = true;
    bool _liveScoreClock = true;
    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Apply(this,_preferences.Theme);
        SportSelector.SelectedValuePath = "Id"; RefreshSports(); SportSelector.SelectedValue = _engine.State.SportId;
        _engine.Changed += Refresh; _engine.Changed += EngineChanged; _timer.Tick += (_, _) => _engine.Tick(); _timer.Start();
        _autosave.Tick += (_, _) => AutoSave(); _autosave.Start();
        _preview = new ScorebugCanvasView(_scorebug,_publishedEngine);
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
        if (SportSelector.SelectedValue is not string selectedId || selectedId != s.SportId) SportSelector.SelectedValue = s.SportId;
        // The log only needs rebuilding when an action was logged; rebuilding it on every clock tick caused visible lag.
        if (s.Events.Count != _eventCount)
        {
            _eventCount = s.Events.Count;
            EventList.ItemsSource = s.Events.AsEnumerable().Reverse().Take(100).Select(x => $"{x.At:HH:mm:ss}  {x.Type,-10} {x.Description}").ToArray();
        }
    }
    static T? Find<T>(DependencyObject parent, string name) where T : FrameworkElement
    { for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i); if(child is T item && item.Name==name)return item; var nested=Find<T>(child,name); if(nested!=null)return nested; } return null; }
    void RefreshSports() { SportSelector.ItemsSource = _engine.Sports.All; }
    void ScorebugStudio_Click(object sender,RoutedEventArgs e) { var studio=new ScorebugEditorWindow(_scorebug,_engine){Owner=this}; ThemeManager.Apply(studio,_preferences.Theme); studio.ShowDialog(); }
    void Preferences_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new SettingsWindow(_preferences){Owner=this};
        ThemeManager.Apply(dialog,_preferences.Theme);
        if(dialog.ShowDialog()!=true)return; // Cancel discards: nothing is applied or saved.
        ThemeManager.Apply(this,_preferences.Theme);
        foreach(var panel in _controlPanels.Where(p=>p.IsVisible))ThemeManager.Apply(panel,_preferences.Theme);
        _output?.SetBroadcastTheme(_preferences.Theme);
    }
    void ControlPanel_Click(object sender,RoutedEventArgs e)
    {
        var existing=_controlPanels.LastOrDefault(p=>p.IsVisible);
        if(existing is not null){existing.Activate();return;}
        OpenControlPanel();
    }
    public void OpenControlPanel()
    {
        var panel=new GameControlWindow(_engine,()=>PublishOutput(),enabled=>SetLiveScoreClock(enabled),()=>_liveScoreClock);
        panel.NewPanelRequested+=OpenControlPanel;
        panel.Closed+=(_,_)=>_controlPanels.Remove(panel);
        ThemeManager.Apply(panel,_preferences.Theme);
        _controlPanels.Add(panel);
        panel.Show();
    }
    void SportProfiles_Click(object sender,RoutedEventArgs e) { var dialog=new SportProfileWindow(_engine.Sports){Owner=this}; ThemeManager.Apply(dialog,_preferences.Theme); dialog.ShowDialog(); var activeId=SportSelector.SelectedValue as string; RefreshSports(); if(_engine.Sports.Find(activeId) is not null)SportSelector.SelectedValue=activeId; }
    void Sport_Changed(object sender, SelectionChangedEventArgs e) { if (_initializing || SportSelector.SelectedValue is not string id) return; _engine.NewGame(id); StatusText.Text = $"NEW {_engine.Sport.Name.ToUpperInvariant()} GAME"; }
    void NewGame_Click(object sender,RoutedEventArgs e) { if(MessageBox.Show("Start a new game? This resets scores and the game clock.","New game",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return; _engine.NewGame(SportSelector.SelectedValue as string ?? "basketball"); StatusText.Text="NEW GAME"; }

    void Output_Click(object sender,RoutedEventArgs e) { if(_output is { IsVisible:true }) { _output.Activate(); return; } _output=new OutputWindow(_publishedEngine,_scorebug,_preferences.Theme); _output.Show(); if(_liveScoreClock)_output.UpdateLiveScoreClock(_engine.State);else _output.RefreshDisplay(); }
    void SetLiveScoreClock(bool enabled)
    {
        _liveScoreClock=enabled;
        foreach(var panel in _controlPanels)panel.SyncLiveState(enabled);
        if(enabled)PublishLiveValues();
    }
    void EngineChanged(){if(_liveScoreClock)PublishLiveValues();}
    void PublishLiveValues()
    {
        if(!_publishedEngine.UpdateLiveScoreClock(_engine.State))return;
        _preview?.Refresh();
        if(_output is null||!_output.IsVisible)return;
        _output.UpdateLiveScoreClock(_engine.State);
    }
    void PublishOutput(){_publishedEngine.LoadFromState(_engine.CloneState());_preview?.Refresh();_output?.Publish();StatusText.Text=$"OUTPUT UPDATED · {DateTime.Now:HH:mm:ss}";}
    void Fullscreen_Click(object sender,RoutedEventArgs e) { Output_Click(sender,e); _output?.ToggleFullscreen(); }
    void GameReport_Click(object sender,RoutedEventArgs e){var dialog=new Microsoft.Win32.SaveFileDialog{Filter="HTML game report (*.html)|*.html",DefaultExt="html",FileName=$"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}-report"};if(dialog.ShowDialog()==true){File.WriteAllText(dialog.FileName,Scoreboard.Core.GameReport.Build(_engine.State));System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName){UseShellExecute=true});StatusText.Text="GAME REPORT EXPORTED";}}
    void Save_Click(object sender,RoutedEventArgs e) { var d=new SaveFileDialog{Filter="Scoreboard Game (*.scoreboard)|*.scoreboard|JSON (*.json)|*.json",DefaultExt="scoreboard",FileName=$"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}"}; if(d.ShowDialog()==true){_engine.Save(d.FileName);_projectPath=d.FileName;StatusText.Text="GAME SAVED";} }
    void Open_Click(object sender,RoutedEventArgs e) { var d=new OpenFileDialog{Filter="Scoreboard Game (*.scoreboard;*.json)|*.scoreboard;*.json|All files (*.*)|*.*"}; if(d.ShowDialog()==true){try{_engine.Load(d.FileName);_projectPath=d.FileName;RefreshSports();SportSelector.SelectedValue=_engine.State.SportId;StatusText.Text="GAME LOADED";}catch(Exception ex){MessageBox.Show($"Could not load game: {ex.Message}");}} }
    void AutoSave() { try { _engine.Save(_recoveryPath); AutosaveText.Text=$"AUTOSAVED {DateTime.Now:HH:mm:ss}"; } catch(Exception ex){StatusText.Text=$"AUTOSAVE WARNING · {ex.Message}";} }
    void Window_KeyDown(object sender,KeyEventArgs e) { if(Keyboard.FocusedElement is TextBox)return; if(e.Key==Key.Z&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){_engine.Undo();e.Handled=true;} }
    protected override void OnClosed(EventArgs e)
    {
        _engine.Changed-=Refresh;_engine.Changed-=EngineChanged;_preview?.Dispose();
        foreach(var panel in _controlPanels.ToArray())panel.Close();
        _output?.Close();
        base.OnClosed(e);
    }
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
