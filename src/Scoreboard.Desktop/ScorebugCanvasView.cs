using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public sealed class ScorebugCanvasView : Grid
{
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly Func<GameState> _stateProvider;
    readonly bool _live;
    readonly Canvas _canvas = new() { Width=1280, Height=720, Background=new SolidColorBrush(Color.FromRgb(9,16,24)) };
    readonly Dictionary<string, (FrameworkElement Host, FrameworkElement Content)> _visuals = [];
    public ScorebugCanvasView(ScorebugCatalog catalog, GameEngine engine, Func<GameState>? stateProvider=null)
    {
        _catalog=catalog;_engine=engine;_live=stateProvider is null;_stateProvider=stateProvider??(()=>_engine.State);
        Children.Add(new Viewbox { Stretch=Stretch.Uniform, Child=_canvas });
        _catalog.Changed+=Rebuild;
        if(stateProvider is null)_engine.Changed+=UpdateValues;else _engine.Changed+=UpdateClockValues;
        Rebuild();
    }
    public void Rebuild()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Rebuild);return;}
        _canvas.Children.Clear();_visuals.Clear();
        foreach(var item in _catalog.Layout.Elements)
        {
            FrameworkElement content;
            if(item.Kind==ScorebugElementKind.Logo&&!string.IsNullOrWhiteSpace(item.ImagePath)&&File.Exists(item.ImagePath))
            {
                try{content=new Image{Source=new BitmapImage(new Uri(item.ImagePath!)),Stretch=Stretch.Uniform};}
                catch{content=MakeText(item);}
            }
            else if(item.Kind==ScorebugElementKind.Shape) content=new Rectangle{Fill=ParseBrush(item.Background),Stroke=ParseBrush(item.Foreground),StrokeThickness=2,RadiusX=10,RadiusY=10};
            else content=MakeText(item);
            var host=new Border{Width=item.Width,Height=item.Height,Opacity=item.Opacity,Background=item.Kind!=ScorebugElementKind.Logo&&!item.TransparentBackground?ParseBrush(item.Background):Brushes.Transparent,Child=content,Visibility=item.Visible?Visibility.Visible:Visibility.Collapsed};
            Canvas.SetLeft(host,item.X);Canvas.SetTop(host,item.Y);Panel.SetZIndex(host,_catalog.Layout.Elements.IndexOf(item));_canvas.Children.Add(host);_visuals[item.Id]=(host,content);
            if(item.Animation==ScorebugAnimation.Pulse)host.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.68,1,TimeSpan.FromSeconds(1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
            if(item.Animation==ScorebugAnimation.Drift)host.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,10,TimeSpan.FromSeconds(2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
            if(item.Animation==ScorebugAnimation.Drift)host.RenderTransform=new TranslateTransform();
        }
        UpdateValues();
    }
    public void Refresh()=>UpdateValues();
    void UpdateClockValues(){if(!_live){_stateProvider().ClockSeconds=_engine.State.ClockSeconds;_stateProvider().ShotClockSeconds=_engine.State.ShotClockSeconds;}_stateProvider().ClockRunning=_engine.State.ClockRunning;_stateProvider().ShotClockRunning=_engine.State.ShotClockRunning;UpdateValues();}
    void UpdateValues()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(UpdateValues);return;}
        var state=_stateProvider();
        foreach(var item in _catalog.Layout.Elements)
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            visual.Host.Visibility=item.Visible?Visibility.Visible:Visibility.Collapsed;
            visual.Host.Opacity=item.Opacity;
            if(visual.Host is Border hostBorder)hostBorder.Background=item.Kind!=ScorebugElementKind.Logo&&!item.TransparentBackground?ParseBrush(item.Background):Brushes.Transparent;
            if(visual.Content is TextBlock text){text.Text=Bind(item,state);text.Foreground=ParseBrush(item.Foreground);text.FontSize=item.FontSize;}
            if(item.Kind==ScorebugElementKind.Shape&&visual.Content is Rectangle shape){shape.Fill=ParseBrush(item.Background);shape.Stroke=ParseBrush(item.Foreground);}
        }
    }
    static TextBlock MakeText(ScorebugElement item)=>new(){Text=item.Content,Foreground=ParseBrush(item.Foreground),FontSize=item.FontSize,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    static string Bind(ScorebugElement item,GameState s)=>ScoreboardTemplates.Bind(item.Content,s);

    static Brush ParseBrush(string value){try{return (Brush)new BrushConverter().ConvertFromString(value)!;}catch{return Brushes.Transparent;}}
    public void Dispose(){_catalog.Changed-=Rebuild;if(_live)_engine.Changed-=UpdateValues;else _engine.Changed-=UpdateClockValues;}
}
