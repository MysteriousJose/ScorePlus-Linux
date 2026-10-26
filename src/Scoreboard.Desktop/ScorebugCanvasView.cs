using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public sealed class ScorebugCanvasView : Grid
{
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly Func<GameState> _stateProvider;
    readonly bool _live;
    readonly Canvas _canvas = new() { Background=new SolidColorBrush(Color.FromRgb(9,16,24)), ClipToBounds=true };
    readonly MediaPlayback? _playback;
    readonly bool _muteVideo;
    readonly Dictionary<string, (FrameworkElement Host, FrameworkElement Content)> _visuals = [];
    readonly List<VideoSurface> _videoSurfaces = [];
    readonly Dictionary<string, (ScorebugAnimation Animation, double Opacity)> _animations = [];
    readonly Dictionary<string,string> _backgrounds = [];
    readonly Dictionary<string,bool> _transparentStates = [];

    public ScorebugCanvasView(ScorebugCatalog catalog, GameEngine engine, Func<GameState>? stateProvider=null, MediaPlayback? playback=null, bool muteVideo=true)
    {
        _catalog=catalog;_engine=engine;_live=stateProvider is null;_stateProvider=stateProvider??(()=>_engine.State);
        _playback=playback;_muteVideo=muteVideo;
        Children.Add(new Viewbox { Stretch=Stretch.Uniform, Child=_canvas });
        _catalog.Changed+=Rebuild;
        if(stateProvider is null)_engine.Changed+=UpdateValues;else _engine.Changed+=UpdateClockValues;
        Rebuild();
    }

    public void Rebuild()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Rebuild);return;}
        foreach(var surface in _videoSurfaces) surface.Dispose();
        _videoSurfaces.Clear();
        _canvas.Width=_catalog.Layout.Width;_canvas.Height=_catalog.Layout.Height;
        _canvas.Children.Clear();_visuals.Clear();_animations.Clear();_backgrounds.Clear();_transparentStates.Clear();
        foreach(var item in _catalog.Layout.Elements)
        {
            var content=ScorebugRender.CreateContent(item,_stateProvider,_playback,_muteVideo);
            if(content is VideoSurface video) _videoSurfaces.Add(video);
            var host=new Border
            {
                Width=item.Width,
                Height=item.Height,
                Opacity=item.Opacity,
                ClipToBounds=true,
                Background=ScorebugRender.HostBackground(item),
                Child=content,
                Visibility=item.Visible?Visibility.Visible:Visibility.Collapsed
            };
            Canvas.SetLeft(host,item.X);Canvas.SetTop(host,item.Y);Panel.SetZIndex(host,_catalog.Layout.Elements.IndexOf(item));
            _canvas.Children.Add(host);_visuals[item.Id]=(host,content);
            ApplyAnimation(item,host);
        }
        UpdateValues();
    }

    void ApplyAnimation(ScorebugElement item,FrameworkElement host)
    {
        if(_animations.TryGetValue(item.Id,out var applied)&&applied.Animation==item.Animation&&Math.Abs(applied.Opacity-item.Opacity)<0.0001)return;
        host.BeginAnimation(UIElement.OpacityProperty,null);
        if(host.RenderTransform is TranslateTransform previous)
        {
            previous.BeginAnimation(TranslateTransform.XProperty,null);
            host.RenderTransform=Transform.Identity;
        }
        switch(item.Animation)
        {
            case ScorebugAnimation.Pulse:
                host.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(item.Opacity*0.68,item.Opacity,TimeSpan.FromSeconds(1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
                break;
            case ScorebugAnimation.Drift:
                host.RenderTransform=new TranslateTransform();
                host.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,10,TimeSpan.FromSeconds(2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
                break;
        }
        _animations[item.Id]=(item.Animation,item.Opacity);
    }

    public void Refresh()=>UpdateValues();

    void UpdateClockValues()
    {
        var state=_stateProvider();
        state.ClockSeconds=_engine.State.ClockSeconds;
        state.ShotClockSeconds=_engine.State.ShotClockSeconds;
        state.ClockRunning=_engine.State.ClockRunning;
        state.ShotClockRunning=_engine.State.ShotClockRunning;
        state.PlayClockSeconds=_engine.State.PlayClockSeconds;
        state.PlayClockRunning=_engine.State.PlayClockRunning;
        state.TimeoutClockSeconds=_engine.State.TimeoutClockSeconds;
        state.TimeoutClockRunning=_engine.State.TimeoutClockRunning;
        UpdateValues();
    }

    void UpdateValues()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(UpdateValues);return;}
        var state=_stateProvider();
        foreach(var item in _catalog.Layout.Elements)
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            var visibility=item.Visible?Visibility.Visible:Visibility.Collapsed;
            if(visual.Host.Visibility!=visibility)visual.Host.Visibility=visibility;
            if(item.Animation!=ScorebugAnimation.Pulse&&Math.Abs(visual.Host.Opacity-item.Opacity)>0.0001)visual.Host.Opacity=item.Opacity;
            if(visual.Host is Border hostBorder)
            {
                // Only touch the brush when the design changed; rebuilding brushes every tick made the output stutter.
                if(!_backgrounds.TryGetValue(item.Id,out var applied)||applied!=item.Background||_transparentStates.GetValueOrDefault(item.Id)!=item.TransparentBackground)
                {
                    hostBorder.Background=ScorebugRender.HostBackground(item);
                    _backgrounds[item.Id]=item.Background;
                    _transparentStates[item.Id]=item.TransparentBackground;
                }
            }
            if(visual.Content is TextBlock block)
            {
                var text=ScoreboardTemplates.Bind(item.Content,state);
                if(block.Text!=text)block.Text=text;
            }
            else if(visual.Content is not VideoSurface) ScorebugRender.ApplyToContent(visual.Content,item);
            ApplyAnimation(item,visual.Host);
        }
    }

    public void Dispose()
    {
        _catalog.Changed-=Rebuild;
        if(_live)_engine.Changed-=UpdateValues;else _engine.Changed-=UpdateClockValues;
        foreach(var surface in _videoSurfaces) surface.Dispose();
        _videoSurfaces.Clear();
    }
}
