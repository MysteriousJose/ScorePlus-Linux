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
    // Which artwork this view draws. The audience output resolves it from its own display; the
    // window preview resolves it from that window's staged selection.
    readonly Func<ScorebugLayout> _layoutProvider;
    readonly Canvas _canvas = new() { Background=new SolidColorBrush(Color.FromRgb(9,16,24)), ClipToBounds=true };
    readonly MediaPlayback? _playback;
    readonly bool _muteVideo;
    readonly Dictionary<string, (FrameworkElement Host, FrameworkElement Content)> _visuals = [];
    // Keyed by element id so a rebuild can keep the surface (and the running picture) alive.
    readonly Dictionary<string, VideoSurface> _videoSurfaces = [];
    readonly Dictionary<string, (ScorebugAnimation Animation, double Opacity)> _animations = [];
    readonly Dictionary<string,string> _backgrounds = [];
    readonly Dictionary<string,bool> _transparentStates = [];
    readonly Dictionary<string,string> _borders = [];
    // Time-of-day text is not driven by the game clock, so it gets its own one-second pulse;
    // otherwise a layout with {{TIME}} freezes whenever no clock is running.
    readonly System.Windows.Threading.DispatcherTimer _wallClock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <param name="publishedLayout">True for the audience output: it follows the layout that was
    /// actually published, not the one being designed in the preview.</param>
    /// <param name="publishedLayout">True for the audience output: it follows the layout that was
    /// actually published, not the one being designed in the preview.</param>
    /// <param name="layoutProvider">Draws exactly the layout this view belongs to (a specific
    /// display's on-air layout, or a window's staged one). Overrides <paramref name="publishedLayout"/>.</param>
    public ScorebugCanvasView(ScorebugCatalog catalog, GameEngine engine, Func<GameState>? stateProvider=null, MediaPlayback? playback=null, bool muteVideo=true, bool publishedLayout=false, Func<ScorebugLayout>? layoutProvider=null)
    {
        _catalog=catalog;_engine=engine;_live=stateProvider is null;
        _layoutProvider = layoutProvider ?? (publishedLayout ? () => _catalog.PublishedLayout : () => _catalog.Layout);
        _stateProvider=stateProvider??(()=>_engine.State);
        _playback=playback;_muteVideo=muteVideo;
        Children.Add(new Viewbox { Stretch=Stretch.Uniform, Child=_canvas });
        _catalog.Changed+=Rebuild;
        if(stateProvider is null)_engine.Changed+=UpdateValues;else _engine.Changed+=UpdateClockValues;
        _wallClock.Tick+=(_,_)=>{if(UsesWallClock())UpdateValues();};
        _wallClock.Start();
        Rebuild();
    }

    /// <summary>The artwork this view is drawing: the design being previewed, or the published one.</summary>
    public ScorebugLayout Layout => _layoutProvider();

    public void Rebuild()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Rebuild);return;}
        var previousVideos=new Dictionary<string,VideoSurface>(_videoSurfaces);
        var previousVisuals=new Dictionary<string,(FrameworkElement Host, FrameworkElement Content)>(_visuals);
        _videoSurfaces.Clear();_visuals.Clear();_animations.Clear();_backgrounds.Clear();_transparentStates.Clear();_borders.Clear();
        var layout = Layout;
        _canvas.Width=layout.Width;_canvas.Height=layout.Height;
        var live=new List<UIElement>();
        var zIndex=0;
        foreach(var item in layout.Elements)
        {
            // The preview and the audience screen stay empty when nothing is cued; the studio is
            // the only place the tag placeholder is drawn.
            // A video box that still exists keeps its live surface. Rebuilding it stopped the
            // decoder and re-opened the file, which is why editing the studio restarted playback.
            FrameworkElement content;
            if(previousVideos.TryGetValue(item.Id,out var surface)){surface.SetElement(item);content=surface;}
            else content=ScorebugRender.CreateContent(item,_stateProvider,_playback,_muteVideo,showVideoPlaceholder:false);
            if(content is VideoSurface video) _videoSurfaces[item.Id]=video;
            // The host border is reused as well. Moving a MediaElement to a new parent takes it out
            // of the visual tree, which stops the decoder and restarts the picture from frame one:
            // that is what made a video jump back to the start on every studio edit.
            var reused=previousVisuals.TryGetValue(item.Id,out var prior)&&ReferenceEquals(prior.Content,content);
            var host=reused?(Border)prior.Host:new Border{ClipToBounds=true,Child=content};
            if(!reused)host.Background=ScorebugRender.HostBackground(item,_stateProvider());
            host.Width=item.Width;host.Height=item.Height;host.Opacity=item.Opacity;
            host.Visibility=item.Visible?Visibility.Visible:Visibility.Collapsed;
            Canvas.SetLeft(host,item.X);Canvas.SetTop(host,item.Y);Panel.SetZIndex(host,zIndex++);
            _visuals[item.Id]=(host,content);
            if(!_canvas.Children.Contains(host))_canvas.Children.Add(host);
            live.Add(host);
            ApplyAnimation(item,host);
        }
        foreach(var child in _canvas.Children.OfType<UIElement>().ToList())
            if(!live.Contains(child))_canvas.Children.Remove(child);
        foreach(var (id,orphan) in previousVideos)
            if(!_videoSurfaces.ContainsKey(id))orphan.Dispose();
        UpdateValues();
    }

    void ApplyAnimation(ScorebugElement item,FrameworkElement host)
    {
        if(_animations.TryGetValue(item.Id,out var applied)&&applied.Animation==item.Animation&&Math.Abs(applied.Opacity-item.Opacity)<0.0001)return;
        host.BeginAnimation(UIElement.OpacityProperty,null);
        if(host.RenderTransform is TranslateTransform previous)
        {
            previous.BeginAnimation(TranslateTransform.XProperty,null);
            previous.BeginAnimation(TranslateTransform.YProperty,null);
            host.RenderTransform=Transform.Identity;
        }
        switch(item.Animation)
        {
            case ScorebugAnimation.Pulse:
                host.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(item.Opacity*0.68,item.Opacity,TimeSpan.FromSeconds(1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
                break;
            case ScorebugAnimation.Drift:
                // The old code animated X on the host instead of on its transform, so drift did
                // nothing at all. Animate the transform itself, on two out-of-phase axes.
                var drift=new TranslateTransform();
                host.RenderTransform=drift;
                drift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,14,TimeSpan.FromSeconds(3.2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                drift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,7,TimeSpan.FromSeconds(2.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                break;
        }
        _animations[item.Id]=(item.Animation,item.Opacity);
    }

    /// <summary>True when any element prints the date or the time of day.</summary>
    bool UsesWallClock()
    {
        foreach(var item in Layout.Elements)
            if(item.Content.Contains("{{TIME}}",StringComparison.OrdinalIgnoreCase)||item.Content.Contains("{{DATE}}",StringComparison.OrdinalIgnoreCase))return true;
        return false;
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
        var layout=Layout;
        foreach(var item in layout.Elements)
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            var visibility=item.Visible?Visibility.Visible:Visibility.Collapsed;
            if(visual.Host.Visibility!=visibility)visual.Host.Visibility=visibility;
            if(item.Animation!=ScorebugAnimation.Pulse&&Math.Abs(visual.Host.Opacity-item.Opacity)>0.0001)visual.Host.Opacity=item.Opacity;
            if(visual.Host is Border hostBorder)
            {
                // Only touch the brush when the design or the palette changed; rebuilding brushes
                // every tick made the output stutter.
                var resolved=ScorebugRender.ResolveBackground(item,state);
                if(!_backgrounds.TryGetValue(item.Id,out var applied)||applied!=resolved||_transparentStates.GetValueOrDefault(item.Id)!=item.TransparentBackground)
                {
                    hostBorder.Background=ScorebugRender.HostBackground(item,state);
                    _backgrounds[item.Id]=resolved;
                    _transparentStates[item.Id]=item.TransparentBackground;
                }
                // An outline is part of the artwork, so the audience sees exactly what the studio
                // designed; shape borders are drawn by the shape itself, its host stays clear.
                if(item.Kind!=ScorebugElementKind.Shape)
                {
                    var thickness=Math.Max(0,item.BorderThickness);
                    var resolvedBorder=ScorebugRender.ResolveBorder(item,state);
                    if(!_borders.TryGetValue(item.Id,out var appliedBorder)||appliedBorder!=resolvedBorder)
                    {
                        hostBorder.BorderBrush=ScorebugRender.ParseBrush(resolvedBorder);
                        _borders[item.Id]=resolvedBorder;
                    }
                    if(Math.Abs(hostBorder.BorderThickness.Left-thickness)>0.01)hostBorder.BorderThickness=new Thickness(thickness);
                }
            }
            if(visual.Content is ScorebugTextBlock block)
            {
                var text=ScoreboardTemplates.Bind(item.Content,state,item.PeriodFormat);
                block.ApplyStyle(item,state);
                block.UpdateText(text,item.Transition,item.TransitionSeconds);
            }
            else if(visual.Content is ScorebugIconView icon)
            {
                icon.Update();
            }
            else if(visual.Content is not VideoSurface)
            {
                ScorebugRender.ApplyToContent(visual.Content,item,state);
            }
            ApplyAnimation(item,visual.Host);
        }
    }

    public void Dispose()
    {
        _wallClock.Stop();
        _catalog.Changed-=Rebuild;
        if(_live)_engine.Changed-=UpdateValues;else _engine.Changed-=UpdateClockValues;
        foreach(var surface in _videoSurfaces.Values) surface.Dispose();
        _videoSurfaces.Clear();
    }
}
