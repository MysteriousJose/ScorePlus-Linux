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
    // A silent monitor of the audience picture (not a preview), so it follows the on-air cue.
    readonly bool _onAirVideo;
    readonly Dictionary<string, (FrameworkElement Host, FrameworkElement Content)> _visuals = [];
    // Keyed by element id so a rebuild can keep the surface (and the running picture) alive.
    readonly Dictionary<string, VideoSurface> _videoSurfaces = [];
    readonly Dictionary<string, (ScorebugAnimation Animation, double Opacity)> _animations = [];
    readonly Dictionary<string,string> _backgrounds = [];
    readonly Dictionary<string,bool> _transparentStates = [];
    readonly Dictionary<string,string> _borders = [];
    // Value switch delay: what each element is currently printing, and the wait before a change
    // lands. The operator sets the delay in Preferences; 0 prints instantly.
    readonly Dictionary<string,string> _shownText = [];
    readonly Dictionary<string,(string Target, System.Windows.Threading.DispatcherTimer Timer)> _pendingText = [];
    bool _flushDelay;
    // Time-of-day text is not driven by the game clock, so it gets its own one-second pulse;
    // otherwise a layout with {{TIME}} freezes whenever no clock is running.
    readonly System.Windows.Threading.DispatcherTimer _wallClock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <param name="publishedLayout">True for the audience output: it follows the layout that was
    /// actually published, not the one being designed in the preview.</param>
    /// <param name="publishedLayout">True for the audience output: it follows the layout that was
    /// actually published, not the one being designed in the preview.</param>
    /// <param name="layoutProvider">Draws exactly the layout this view belongs to (a specific
    /// display's on-air layout, or a window's staged one). Overrides <paramref name="publishedLayout"/>.</param>
    public ScorebugCanvasView(ScorebugCatalog catalog, GameEngine engine, Func<GameState>? stateProvider=null, MediaPlayback? playback=null, bool muteVideo=true, bool publishedLayout=false, Func<ScorebugLayout>? layoutProvider=null, bool onAirVideo=false)
    {
        _catalog=catalog;_engine=engine;_live=stateProvider is null;
        _layoutProvider = layoutProvider ?? (publishedLayout ? () => _catalog.PublishedLayout : () => _catalog.Layout);
        _stateProvider=stateProvider??(()=>_engine.State);
        _playback=playback;_muteVideo=muteVideo;_onAirVideo=onAirVideo;
        Children.Add(new Viewbox { Stretch=Stretch.Uniform, Child=_canvas });
        _catalog.Changed+=Rebuild;
        // A roster edit changes what a specific-player binding prints, without touching the game.
        RosterStore.Changed+=UpdateValues;
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
        foreach(var (id,_) in _pendingText) CancelPending(id);
        _videoSurfaces.Clear();_visuals.Clear();_animations.Clear();_backgrounds.Clear();_transparentStates.Clear();_borders.Clear();_shownText.Clear();
        var layout = Layout;
        _canvas.Width=layout.Width;_canvas.Height=layout.Height;
        // The artwork backdrop belongs to the layout, so a green-screen design and the studio agree.
        _canvas.Background=ScorebugRender.ParseBrush(layout.Background);
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
            else content=ScorebugRender.CreateContent(item,_stateProvider,_playback,_muteVideo,showVideoPlaceholder:false,onAirVideo:_onAirVideo);
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
            // A rebuilt view starts from what it draws right now, so a delay never blanks the graphic.
            if(content is ScorebugTextBlock shown) _shownText[item.Id]=shown.Text;
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

    /// <summary>
    /// Prints the newest values at once, skipping the switch delay. Used when the delay itself is
    /// changed, so the new timing starts from what is actually on screen.
    /// </summary>
    public void ApplyNow()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(ApplyNow);return;}
        foreach(var id in _pendingText.Keys.ToArray()) CancelPending(id);
        _flushDelay=true;
        try { UpdateValues(); } finally { _flushDelay=false; }
    }

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
                var resolved=ScorebugRender.BackgroundSignature(item,state);
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
                ShowValue(item,block,text);
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

    /// <summary>
    /// Prints a value, or holds it for the show's switch delay first. While a value waits, newer
    /// values replace the queued one, so the graphic always lands on the newest value in one step
    /// rather than replaying a burst of stale numbers.
    /// </summary>
    void ShowValue(ScorebugElement item,ScorebugTextBlock block,string text)
    {
        var delay=DisplayTiming.ValueSwitchDelay;
        if(_flushDelay||delay<=0||!double.IsFinite(delay))
        {
            CancelPending(item.Id);
            block.UpdateText(text,item.Transition,item.TransitionSeconds);
            _shownText[item.Id]=text;
            return;
        }
        var shown=_shownText.GetValueOrDefault(item.Id,"");
        if(text==shown){CancelPending(item.Id);return;}
        if(_pendingText.TryGetValue(item.Id,out var pending))
        {
            // Already waiting: keep the newest target, leave the timer alone.
            _pendingText[item.Id]=(text,pending.Timer);
            return;
        }
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(Math.Clamp(delay,0.05,10))};
        timer.Tick+=(_,_)=>{
            timer.Stop();
            var latest=_pendingText.TryGetValue(item.Id,out var queued)?queued.Target:text;
            _pendingText.Remove(item.Id);
            block.UpdateText(latest,item.Transition,item.TransitionSeconds);
            _shownText[item.Id]=latest;
        };
        _pendingText[item.Id]=(text,timer);
        timer.Start();
    }

    void CancelPending(string id)
    {
        if(_pendingText.Remove(id,out var pending))pending.Timer.Stop();
    }

    public void Dispose()
    {
        _wallClock.Stop();
        foreach(var (id,_) in _pendingText.ToArray()) CancelPending(id);
        _catalog.Changed-=Rebuild;
        RosterStore.Changed-=UpdateValues;
        if(_live)_engine.Changed-=UpdateValues;else _engine.Changed-=UpdateClockValues;
        foreach(var surface in _videoSurfaces.Values) surface.Dispose();
        _videoSurfaces.Clear();
    }
}
