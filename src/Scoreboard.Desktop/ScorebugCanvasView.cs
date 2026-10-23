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
    readonly Canvas _canvas = new() { Width=1280, Height=720, Background=new SolidColorBrush(Color.FromRgb(9,16,24)) };
    readonly Dictionary<string, (FrameworkElement Host, FrameworkElement Content)> _visuals = [];
    public ScorebugCanvasView(ScorebugCatalog catalog, GameEngine engine)
    {
        _catalog=catalog;_engine=engine;
        Children.Add(new Viewbox { Stretch=Stretch.Uniform, Child=_canvas });
        _catalog.Changed+=Rebuild;_engine.Changed+=UpdateValues;
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
            var host=new Border{Width=item.Width,Height=item.Height,Opacity=item.Opacity,Background=item.Kind==ScorebugElementKind.Text?ParseBrush(item.Background):Brushes.Transparent,Child=content,Visibility=item.Visible?Visibility.Visible:Visibility.Collapsed};
            Canvas.SetLeft(host,item.X);Canvas.SetTop(host,item.Y);Panel.SetZIndex(host,_catalog.Layout.Elements.IndexOf(item));_canvas.Children.Add(host);_visuals[item.Id]=(host,content);
            if(item.Animation==ScorebugAnimation.Pulse)host.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.68,1,TimeSpan.FromSeconds(1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
            if(item.Animation==ScorebugAnimation.Drift)host.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,10,TimeSpan.FromSeconds(2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
            if(item.Animation==ScorebugAnimation.Drift)host.RenderTransform=new TranslateTransform();
        }
        UpdateValues();
    }
    void UpdateValues()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(UpdateValues);return;}
        var state=_engine.State;
        foreach(var item in _catalog.Layout.Elements)
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            visual.Host.Visibility=item.Visible?Visibility.Visible:Visibility.Collapsed;
            if(visual.Content is TextBlock text)text.Text=Bind(item,state);
        }
    }
    static TextBlock MakeText(ScorebugElement item)=>new(){Text=item.Content,Foreground=ParseBrush(item.Foreground),FontSize=item.FontSize,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    static string Bind(ScorebugElement item,GameState s)=>item.Content.Replace("{{HOME_TEAM}}",s.HomeAbbreviation).Replace("{{AWAY_TEAM}}",s.AwayAbbreviation).Replace("{{HOME_SCORE}}",s.HomeScore.ToString()).Replace("{{AWAY_SCORE}}",s.AwayScore.ToString()).Replace("{{PERIOD}}",$"{s.PeriodLabel.ToUpperInvariant()} {s.Period}").Replace("{{GAME_CLOCK}}",GameEngine.FormatClock(s.ClockSeconds)).Replace("{{DATE}}",DateTime.Now.ToString("yyyy-MM-dd")).Replace("{{TIME}}",DateTime.Now.ToString("HH:mm:ss"));
    static Brush ParseBrush(string value){try{return (Brush)new BrushConverter().ConvertFromString(value)!;}catch{return Brushes.Transparent;}}
    public void Dispose(){_catalog.Changed-=Rebuild;_engine.Changed-=UpdateValues;}
}
