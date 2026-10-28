using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;
public partial class ScorebugEditorWindow : Window
{
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly MediaPlayback? _playback;
    readonly string _theme;
    // Keyed by element id: a rebuild reuses the surface so a playing clip is never restarted.
    readonly Dictionary<string, VideoSurface> _videoSurfaces = [];
    // Property edits save through the catalog; the resulting Changed event must not rebuild the
    // whole canvas (and the video surfaces with it). RefreshAllVisuals repaints in place instead.
    bool _skipRebuild;
    readonly System.Windows.Threading.DispatcherTimer _wallClock = new() { Interval=TimeSpan.FromSeconds(1) };
    bool _canvasLoading;
    bool _layoutLoading;
    bool _displayLoading;
    bool _linkLoading;
    // Speakers are listed once per studio window; asking WASAPI per keystroke would be wasteful.
    readonly IReadOnlyList<AudioOption> _audioOptions;
    readonly System.Windows.Threading.DispatcherTimer _propertySave = new() { Interval=TimeSpan.FromMilliseconds(350) };
    readonly Dictionary<string,(Border Host, FrameworkElement Content, Border Handle, ScorebugElementKind Kind)> _visuals = [];
    readonly Dictionary<string,(ScorebugElementKind Kind, ScorebugShape Shape, string? Image, ScorebugAnimation Animation, double Opacity)> _applied = [];
    bool _loading;
    bool _syncingLayers;
    // Everything selected, in the order it was picked; the primary is the one the property panel
    // edits and the one whose resize handle is shown.
    readonly List<string> _selectedIds = [];
    string? _selectedId;
    ScorebugElement? _dragElement;
    // Where each selected element started, so dragging one object carries the whole selection.
    readonly Dictionary<string,(double X, double Y)> _dragOrigins = [];
    Point _dragStartPoint;
    bool _resizing;
    double _resizeStartX, _resizeStartY, _resizeStartWidth, _resizeStartHeight;
    Point _listStart;

    sealed record PeriodOption(string? Key, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record ColorSourceOption(ScorebugColorSource Source, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record LogoOption(ScorebugLogoSide Side, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>A speaker offered in the element's audio picker.</summary>
    sealed record AudioOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record DisplayOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record LinkDisplayOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record LinkLayoutOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    // Per-element quarter style. "Follow game default" keeps the old behaviour for layouts that
    // never asked for anything else.
    static readonly PeriodOption[] PeriodOptions =
    [
        new(null, "Follow game default"),
        new("Full", "Quarter 1"),
        new("Short", "Q1"),
        new("Number", "1")
    ];

    static readonly ColorSourceOption[] ColorSources =
    [
        new(ScorebugColorSource.Custom, "Custom colour"),
        new(ScorebugColorSource.HomePrimary, "Home · primary"),
        new(ScorebugColorSource.HomeSecondary, "Home · secondary"),
        new(ScorebugColorSource.AwayPrimary, "Visitors · primary"),
        new(ScorebugColorSource.AwaySecondary, "Visitors · secondary")
    ];

    static readonly LogoOption[] Logos =
    [
        new(ScorebugLogoSide.Home, "Home team logo"),
        new(ScorebugLogoSide.Away, "Visitors team logo")
    ];

    public ScorebugEditorWindow(ScorebugCatalog catalog, GameEngine engine, MediaPlayback? playback=null, string theme="Dark")
    {
        InitializeComponent(); _catalog=catalog; _engine=engine; _playback=playback; _theme=theme;
        ThemeManager.Apply(this,theme);
        AnimationInput.ItemsSource=Enum.GetValues<ScorebugAnimation>();
        TransitionInput.ItemsSource=Enum.GetValues<ScorebugTransition>();
        ShapeInput.ItemsSource=Enum.GetValues<ScorebugShape>();
        IconInput.ItemsSource=Enum.GetValues<ScorebugIcon>();
        PeriodFormatInput.ItemsSource=PeriodOptions;
        ForegroundSourceInput.ItemsSource=ColorSources;
        BackgroundSourceInput.ItemsSource=ColorSources;
        BorderSourceInput.ItemsSource=ColorSources;
        LogoInput.ItemsSource=Logos;
        FontInput.ItemsSource=FontCatalog.All;
        _audioOptions=new[]{new AudioOption(null,"Default output")}
            .Concat(AudioRouter.Devices().Select(device=>new AudioOption(device.Id,device.Name))).ToArray();
        AudioDeviceInput.ItemsSource=_audioOptions;
        CanvasPreset.ItemsSource=ScorebugLayout.SizePresets.Select(p=>p.Name).ToArray();
        _canvasLoading=true;
        CanvasPreset.SelectedIndex=PresetIndex(catalog.Layout.Width,catalog.Layout.Height);
        CanvasWidthInput.Text=catalog.Layout.Width.ToString();
        CanvasHeightInput.Text=catalog.Layout.Height.ToString();
        _canvasLoading=false;
        _propertySave.Tick+=(_,_)=>{_propertySave.Stop();SaveProperties();RefreshAllVisuals();};
        _catalog.Changed += Render;
        _engine.Changed += RefreshBoundText;
        _wallClock.Tick+=(_,_)=>{if(UsesWallClock())RefreshBoundText();};
        _wallClock.Start();
        Render();
    }

    /// <summary>True when any element prints the date or the time of day.</summary>
    bool UsesWallClock()
    {
        foreach(var item in _catalog.Layout.Elements)
            if(item.Content.Contains("{{TIME}}",StringComparison.OrdinalIgnoreCase)||item.Content.Contains("{{DATE}}",StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }

    ScorebugElement? Selected => _selectedId is null ? null : _catalog.Layout.Elements.FirstOrDefault(x=>x.Id==_selectedId);

    bool IsSelected(string id) => _selectedIds.Contains(id);

    /// <summary>Drops the selection entirely, for when the layout underneath it goes away.</summary>
    void ClearSelection()
    {
        _selectedIds.Clear();
        _selectedId=null;
    }

    /// <summary>
    /// Mirrors the canvas selection onto the layer list without treating it as the operator's click.
    /// The list runs in Extended mode, so Ctrl and Shift give range and toggle selection for free.
    /// </summary>
    void SyncLayersFromSelection()
    {
        _syncingLayers=true;
        try
        {
            Layers.SelectedItems.Clear();
            foreach(var item in Layers.Items.OfType<LayerItem>().Where(x=>_selectedIds.Contains(x.Element.Id)))
                Layers.SelectedItems.Add(item);
        }
        finally { _syncingLayers=false; }
    }

    void Render()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Render);return;}
        if(_skipRebuild)return;
        if(_dragElement is not null)return; // never rebuild under an active gesture
        _selectedIds.RemoveAll(id=>_catalog.Layout.Elements.All(x=>x.Id!=id));
        if(_selectedId is not null && !_selectedIds.Contains(_selectedId))_selectedId=_selectedIds.LastOrDefault();
        _loading=true;
        _syncingLayers=true;
        // Top of the list is the front of the picture, the opposite of the canvas' back-to-front order.
        var ordered=_catalog.Layout.Elements.AsEnumerable().Reverse().Select(e=>new LayerItem(e)).ToArray();
        Layers.ItemsSource=ordered;
        foreach(var item in ordered.Where(x=>_selectedIds.Contains(x.Element.Id)))Layers.SelectedItems.Add(item);
        LayerCount.Text=ordered.Length==0?"no elements":$"{ordered.Length} element{(ordered.Length==1?"":"s")}";
        _syncingLayers=false;
        SyncLayoutPicker();
        SyncDisplayPicker();
        SyncLinkPicker();
        var previousVideos=new Dictionary<string,VideoSurface>(_videoSurfaces);
        var previousVisuals=new Dictionary<string,(Border Host, FrameworkElement Content, Border Handle, ScorebugElementKind Kind)>(_visuals);
        _videoSurfaces.Clear();
        DesignCanvas.Width=_catalog.Layout.Width; DesignCanvas.Height=_catalog.Layout.Height;
        // Elements (video especially) must never paint outside the artwork they belong to.
        DesignCanvas.ClipToBounds=true;
        CanvasInfo.Text=$"{_catalog.Layout.Name} · {_catalog.Layout.Width} × {_catalog.Layout.Height} · drag elements to reposition";
        if(!_canvasLoading)
        {
            _canvasLoading=true;
            CanvasWidthInput.Text=_catalog.Layout.Width.ToString();
            CanvasHeightInput.Text=_catalog.Layout.Height.ToString();
            CanvasPreset.SelectedIndex=PresetIndex(_catalog.Layout.Width,_catalog.Layout.Height);
            _canvasLoading=false;
        }
        // Reuse the visual for every element that still exists instead of clearing the canvas. Taking
        // a MediaElement out of the visual tree stops its decoder, so a full rebuild on each edit
        // restarted every video and left the dock's playhead behind it.
        _visuals.Clear(); _applied.Clear();
        var zIndex=0;
        foreach(var item in _catalog.Layout.Elements)
        {
            Border host;
            if(previousVisuals.TryGetValue(item.Id,out var prior)&&prior.Kind==item.Kind)
            {
                host=prior.Host;
                if(prior.Content is VideoSurface video)video.SetElement(item);
                _visuals[item.Id]=(host,prior.Content,prior.Handle,item.Kind);
                if(prior.Content is VideoSurface kept)_videoSurfaces[item.Id]=kept;
            }
            else host=CreateVisual(item,previousVideos);
            // A new visual is added here; a reused one is already on the canvas and must stay put,
            // because removing it would take a playing video out of the tree.
            if(!DesignCanvas.Children.Contains(host))DesignCanvas.Children.Add(host);
            Panel.SetZIndex(host,zIndex++);
        }
        foreach(var child in DesignCanvas.Children.OfType<UIElement>().ToList())
            if(!_visuals.Values.Any(visual=>ReferenceEquals(visual.Host,child)))DesignCanvas.Children.Remove(child);
        foreach(var (id,surface) in previousVideos)
            if(!_videoSurfaces.ContainsKey(id))surface.Dispose();
        RefreshAllVisuals();
        _loading=false;
        LoadProperties();
    }

    /// <summary>Keeps the layout picker in step without treating the sync as the operator's choice.</summary>
    void SyncLayoutPicker()
    {
        _layoutLoading=true;
        try
        {
            var names=_catalog.Layouts.Select((layout,index)=>$"{index+1}. {layout.Name}").ToArray();
            // Replacing ItemsSource during its own SelectionChanged resets the picker, so only
            // rebuild the list when the layouts themselves changed.
            if(LayoutBox.ItemsSource is not IEnumerable<string> existing||!existing.SequenceEqual(names))
                LayoutBox.ItemsSource=names;
            LayoutBox.SelectedIndex=_catalog.ActiveIndex;
            LayoutHintText.Text=_catalog.ActiveIndex==_catalog.PublishedIndex
                ? "This layout is on air."
                : "Preview only — press Update Output in the control panel to put it on air.";
        }
        finally { _layoutLoading=false; }
    }

    /// <summary>Fills the display picker from the catalog without treating the sync as a choice.</summary>
    void SyncDisplayPicker()
    {
        _displayLoading=true;
        try
        {
            var options=_catalog.Displays.Select((display,index)=>new DisplayOption(display.Id,$"{index+1}. {display.Name}")).ToArray();
            if(DisplayBox.ItemsSource is not IEnumerable<DisplayOption> existing||!existing.Select(x=>x.Label).SequenceEqual(options.Select(x=>x.Label)))
                DisplayBox.ItemsSource=options;
            DisplayBox.SelectedIndex=_catalog.ActiveDisplayIndex;
        }
        finally { _displayLoading=false; }
    }

    /// <summary>Shows this layout's link to another display, if it has one that still resolves.</summary>
    void SyncLinkPicker()
    {
        _linkLoading=true;
        try
        {
            var layout=_catalog.Layout;
            var targets=new List<LinkDisplayOption>{new(null,"No link")};
            foreach(var display in _catalog.Displays)
            {
                if(string.Equals(display.Id,_catalog.ActiveDisplay.Id,StringComparison.OrdinalIgnoreCase))continue;
                targets.Add(new LinkDisplayOption(display.Id,display.Name));
            }
            LinkDisplayBox.ItemsSource=targets;
            var selected=targets.FirstOrDefault(option=>string.Equals(option.Id,layout.LinkDisplayId,StringComparison.OrdinalIgnoreCase))??targets[0];
            LinkDisplayBox.SelectedItem=selected;
            FillLinkLayouts(selected.Id,layout.LinkLayoutId);
            // Ticked exactly when the layout on the other side already points back at this one.
            LinkBothWaysInput.IsChecked=HasReciprocal(layout);
        }
        finally { _linkLoading=false; }
    }

    /// <summary>True when the layout this one links to links straight back to it.</summary>
    bool HasReciprocal(ScorebugLayout layout)
    {
        if(string.IsNullOrWhiteSpace(layout.LinkDisplayId)||string.IsNullOrWhiteSpace(layout.LinkLayoutId))return false;
        var target=_catalog.FindDisplay(layout.LinkDisplayId);
        var counterpart=target?.Layouts.FirstOrDefault(x=>string.Equals(x.Id,layout.LinkLayoutId,StringComparison.OrdinalIgnoreCase));
        return counterpart is not null
            &&string.Equals(counterpart.LinkDisplayId,_catalog.ActiveDisplay.Id,StringComparison.OrdinalIgnoreCase)
            &&string.Equals(counterpart.LinkLayoutId,layout.Id,StringComparison.OrdinalIgnoreCase);
    }

    void LinkBothWays_Changed(object sender,RoutedEventArgs e)
    {
        if(_linkLoading)return;
        ApplyReciprocal(LinkBothWaysInput.IsChecked==true);
    }

    /// <summary>
    /// Writes (or removes) the link coming back from the other display's layout, so a link can work
    /// in both directions. Only ever touches the exact layout this one points at.
    /// </summary>
    void ApplyReciprocal(bool enabled)
    {
        var layout=_catalog.Layout;
        var display=_catalog.ActiveDisplay;
        if(string.IsNullOrWhiteSpace(layout.LinkDisplayId)||string.IsNullOrWhiteSpace(layout.LinkLayoutId))return;
        var target=_catalog.FindDisplay(layout.LinkDisplayId);
        if(target is null)return;
        var index=target.Layouts.FindIndex(x=>string.Equals(x.Id,layout.LinkLayoutId,StringComparison.OrdinalIgnoreCase));
        if(index<0)return;
        var back=target.Layouts[index];
        var pointsBack=string.Equals(back.LinkDisplayId,display.Id,StringComparison.OrdinalIgnoreCase)
            &&string.Equals(back.LinkLayoutId,layout.Id,StringComparison.OrdinalIgnoreCase);
        if(enabled&&!pointsBack)_catalog.SetLink(target.Id,index,display.Id,layout.Id);
        else if(!enabled&&pointsBack)_catalog.SetLink(target.Id,index,null,null);
    }

    void FillLinkLayouts(string? displayId,string? selectedLayoutId)
    {
        var target=_catalog.FindDisplay(displayId);
        var options=new List<LinkLayoutOption>{new(null,target is null?"—":"Choose a layout")};
        if(target is not null)
            options.AddRange(target.Layouts.Select((layout,index)=>new LinkLayoutOption(layout.Id,$"{index+1}. {layout.Name}")));
        LinkLayoutBox.ItemsSource=options;
        LinkLayoutBox.IsEnabled=target is not null;
        LinkLayoutBox.SelectedItem=options.FirstOrDefault(option=>string.Equals(option.Id,selectedLayoutId,StringComparison.OrdinalIgnoreCase))??options[0];
    }

    Border CreateVisual(ScorebugElement item,Dictionary<string,VideoSurface>? previous=null)
    {
        FrameworkElement content;
        if(previous is not null&&previous.TryGetValue(item.Id,out var kept))
        {
            // Detach from the old host and re-point at the edited element: the picture keeps running.
            if(kept.Parent is Border oldClip)oldClip.Child=null;
            kept.SetElement(item);
            content=kept;
        }
        else content=ScorebugRender.CreateContent(item,()=>_engine.State,_playback,true,showVideoPlaceholder:true);
        if(content is VideoSurface videoSurface)_videoSurfaces[item.Id]=videoSurface;
        var handle=new Border
        {
            Width=16, Height=16, Background=Brushes.DeepSkyBlue, BorderBrush=Brushes.White,
            BorderThickness=new Thickness(1), Cursor=Cursors.SizeNWSE, Tag=item,
            HorizontalAlignment=HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Bottom,
            Margin=new Thickness(0,0,1,1),
            Visibility=IsSelected(item.Id)&&item.Id==_selectedId?Visibility.Visible:Visibility.Collapsed,
            ToolTip="Drag to resize"
        };
        handle.PreviewMouseLeftButtonDown+=Resize_MouseDown;
        handle.PreviewMouseMove+=Resize_MouseMove;
        handle.PreviewMouseLeftButtonUp+=Resize_MouseUp;
        // Clip the content to its own box so a playing video cannot bleed over neighbouring elements.
        var clipped=new Border{Child=content,ClipToBounds=true};
        var stack=new Grid();
        stack.Children.Add(clipped);
        stack.Children.Add(handle);
        var host=new Border
        {
            Width=item.Width, Height=item.Height, Background=ScorebugRender.HostBackground(item,_engine.State),
            ClipToBounds=true,
            Opacity=item.Opacity, Cursor=Cursors.SizeAll, Tag=item, Child=stack,
            BorderBrush=IsSelected(item.Id)?Brushes.DeepSkyBlue:ScorebugRender.HostBorder(item,_engine.State),
            BorderThickness=new Thickness(IsSelected(item.Id)?2:Math.Max(1,item.BorderThickness))
        };
        Canvas.SetLeft(host,item.X); Canvas.SetTop(host,item.Y); Panel.SetZIndex(host,_catalog.Layout.Elements.IndexOf(item));
        host.MouseLeftButtonDown+=Element_MouseDown; host.MouseMove+=Element_MouseMove; host.MouseLeftButtonUp+=Element_MouseUp;
        _visuals[item.Id]=(host,content,handle,item.Kind);
        ApplyAnimation(item,host);
        return host;
    }

    void ApplyAnimation(ScorebugElement item,FrameworkElement host)
    {
        if(_applied.TryGetValue(item.Id,out var applied)&&applied.Animation==item.Animation&&Math.Abs(applied.Opacity-item.Opacity)<0.0001)return;
        host.BeginAnimation(UIElement.OpacityProperty,null);
        if(host.RenderTransform is TranslateTransform previous){previous.BeginAnimation(TranslateTransform.XProperty,null);previous.BeginAnimation(TranslateTransform.YProperty,null);host.RenderTransform=Transform.Identity;}
        switch(item.Animation)
        {
            case ScorebugAnimation.Pulse:
                host.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(item.Opacity*0.72,item.Opacity,TimeSpan.FromSeconds(1.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
                break;
            case ScorebugAnimation.Drift:
                // Animate the transform, not the host: the old call animated X on the Border itself,
                // which moved nothing, so Drift looked broken.
                var drift=new TranslateTransform();
                host.RenderTransform=drift;
                drift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,14,TimeSpan.FromSeconds(3.2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                drift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,7,TimeSpan.FromSeconds(2.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                break;
        }
        _applied[item.Id]=(item.Kind,item.Shape,item.ImagePath,item.Animation,item.Opacity);
    }

    /// <summary>Updates every visual in place. Used while typing so focus and selection are never lost.</summary>
    void RefreshAllVisuals()
    {
        foreach(var item in _catalog.Layout.Elements)
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            if(visual.Content is ScorebugTextBlock block)
                ScorebugRender.ApplyToContent(block,item,_engine.State,ScoreboardTemplates.Bind(item.Content,_engine.State,item.PeriodFormat));
            else if(visual.Content is ScorebugIconView iconView)
                iconView.Update();
            else if(visual.Content is not VideoSurface)
                ScorebugRender.ApplyToContent(visual.Content,item,_engine.State);
            visual.Host.Width=item.Width; visual.Host.Height=item.Height;
            visual.Host.Background=ScorebugRender.HostBackground(item,_engine.State);
            if(item.Animation!=ScorebugAnimation.Pulse)visual.Host.Opacity=item.Opacity;
            Canvas.SetLeft(visual.Host,item.X); Canvas.SetTop(visual.Host,item.Y);
            visual.Handle.Visibility=item.Id==_selectedId&&IsSelected(item.Id)?Visibility.Visible:Visibility.Collapsed;
            visual.Host.BorderBrush=IsSelected(item.Id)?Brushes.DeepSkyBlue:ScorebugRender.HostBorder(item,_engine.State);
            visual.Host.BorderThickness=new Thickness(IsSelected(item.Id)?2:Math.Max(1,item.BorderThickness));
            ApplyAnimation(item,visual.Host);
        }
        // The property panel's colour previews follow the typed colours in place, since an edit no
        // longer rebuilds the canvas and therefore no longer reloads the panel.
        RefreshColorSwatches();
    }

    void RefreshBoundText()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(RefreshBoundText);return;}
        if(_dragElement is not null)return;
        foreach(var item in _catalog.Layout.Elements)
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            if(visual.Content is ScorebugIconView iconView) iconView.Update();
            else if(visual.Content is ScorebugTextBlock block)
            {
                block.ApplyStyle(item,_engine.State);
                block.UpdateText(ScoreboardTemplates.Bind(item.Content,_engine.State,item.PeriodFormat),item.Transition,item.TransitionSeconds);
            }
            else if(visual.Content is Image or ScorebugLogoView or System.Windows.Shapes.Shape)
                ScorebugRender.ApplyToContent(visual.Content,item,_engine.State);
        }
        RefreshColorSwatches();
    }

    void Layers_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_syncingLayers)return;
        // Extended mode hands over the whole range: Ctrl adds rows one at a time, Shift takes every
        // row between the anchor and the click.
        var ids=Layers.SelectedItems.OfType<LayerItem>().Select(x=>x.Element.Id).ToArray();
        _selectedIds.Clear();
        _selectedIds.AddRange(ids);
        // Keep editing the element the panel already held while it is still selected, so adding a
        // row to the selection does not shuffle the property fields.
        _selectedId=_selectedId is not null&&_selectedIds.Contains(_selectedId) ? _selectedId : ids.LastOrDefault();
        RefreshAllVisuals();
        LoadProperties();
    }

    void LoadProperties()
    {
        _loading=true;
        var e=Selected; var has=e is not null;
        foreach(var control in new Control[]{NameInput,ContentInput,XInput,YInput,WidthInput,HeightInput,FontInput,FontInputSize,RotationInput,ColorInput,BackgroundInput,BorderWidthInput,BorderColorInput,BorderSourceInput,AnimationInput,TransitionInput,TransitionSpeedInput,ShapeInput,VisibleInput,TransparentBackgroundInput,MediaTagInput,PeriodFormatInput,IconInput,IconVisitorsInput,IconFlipInput,ForegroundSourceInput,BackgroundSourceInput,LogoInput,BoldInput,ItalicInput,UnderlineInput,StrikeInput,PreserveAspectInput,AspectInput,AudioDeviceInput})
            control.IsEnabled=has;
        if(has)
        {
            NameInput.Text=e!.Name; ContentInput.Text=e.Content; XInput.Text=e.X.ToString("0"); YInput.Text=e.Y.ToString("0");
            WidthInput.Text=e.Width.ToString("0"); HeightInput.Text=e.Height.ToString("0");
            FontInputSize.Text=e.FontSize.ToString("0");
            RotationInput.Text=e.Rotation.ToString("0");
            var font=FontCatalog.ChoiceFor(e.FontFamily);
            FontInput.SelectedItem=FontCatalog.All.FirstOrDefault(c=>string.Equals(c.Source,font.Source,StringComparison.OrdinalIgnoreCase))??font;
            BoldInput.IsChecked=e.Bold; ItalicInput.IsChecked=e.Italic;
            UnderlineInput.IsChecked=e.Underline; StrikeInput.IsChecked=e.StrikeThrough;
            ColorInput.Text=e.Foreground; BackgroundInput.Text=e.Background;
            BorderColorInput.Text=e.BorderColor; BorderWidthInput.Text=e.BorderThickness.ToString("0.#");
            TransitionSpeedInput.Text=e.TransitionSeconds.ToString("0.00");
            ForegroundSourceInput.SelectedItem=ColorSources.FirstOrDefault(c=>c.Source==e.ForegroundSource)??ColorSources[0];
            BackgroundSourceInput.SelectedItem=ColorSources.FirstOrDefault(c=>c.Source==e.BackgroundSource)??ColorSources[0];
            BorderSourceInput.SelectedItem=ColorSources.FirstOrDefault(c=>c.Source==e.BorderSource)??ColorSources[0];
            UpdateColorAvailability();
            AnimationInput.SelectedItem=e.Animation; TransitionInput.SelectedItem=e.Transition; ShapeInput.SelectedItem=e.Shape;
            VisibleInput.IsChecked=e.Visible; TransparentBackgroundInput.IsChecked=e.TransparentBackground;
            MediaTagInput.Text=e.MediaTag;
            PreserveAspectInput.IsChecked=e.PreserveVideoAspect;
            AspectInput.Text=e.VideoAspect.ToString("0.###");
            AudioDeviceInput.SelectedItem=_audioOptions.FirstOrDefault(option=>string.Equals(option.Id??"",e.AudioDevice??"",StringComparison.OrdinalIgnoreCase))??_audioOptions[0];
            LogoInput.SelectedItem=Logos.FirstOrDefault(l=>l.Side==e.LogoSide)??Logos[0];
            PeriodFormatInput.SelectedItem=PeriodOptions.FirstOrDefault(o=>o.Key==e.PeriodFormat)??PeriodOptions[0];
            IconInput.SelectedItem=e.Icon; IconVisitorsInput.IsChecked=e.IconVisitors; IconFlipInput.IsChecked=e.IconFlipped;
        }
        _loading=false;
        UpdatePropertyVisibility();
        RefreshColorSwatches();
    }

    /// <summary>
    /// Paints the two colour previews in the property panel. A hex box on its own never showed
    /// what an element (or a team palette slot) actually draws; these swatches do.
    /// </summary>
    void RefreshColorSwatches()
    {
        var item=Selected;
        TextColorSwatch.Background=item is null?Brushes.Transparent:ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item,_engine.State));
        BackgroundColorSwatch.Background=item is null||item.TransparentBackground?Brushes.Transparent:ScorebugRender.ParseBrush(ScorebugRender.ResolveBackground(item,_engine.State));
        BorderColorSwatch.Background=item is null||item.BorderThickness<=0?Brushes.Transparent:ScorebugRender.ParseBrush(ScorebugRender.ResolveBorder(item,_engine.State));
        TextColorSwatch.ToolTip=item is null?null:$"Text colour: {ScorebugRender.ResolveForeground(item,_engine.State)}";
        BackgroundColorSwatch.ToolTip=item is null?null:item.TransparentBackground?"No background box":$"Background colour: {ScorebugRender.ResolveBackground(item,_engine.State)}";
        BorderColorSwatch.ToolTip=item is null||item.BorderThickness<=0?"No border":$"Border colour: {ScorebugRender.ResolveBorder(item,_engine.State)}";
    }

    /// <summary>A colour box only takes typing when the element uses a fixed colour.</summary>
    void UpdateColorAvailability()
    {
        ColorInput.IsEnabled=Selected is not null && (ForegroundSourceInput.SelectedItem as ColorSourceOption)?.Source==ScorebugColorSource.Custom;
        BackgroundInput.IsEnabled=Selected is not null && (BackgroundSourceInput.SelectedItem as ColorSourceOption)?.Source==ScorebugColorSource.Custom;
        BorderColorInput.IsEnabled=Selected is not null && (BorderSourceInput.SelectedItem as ColorSourceOption)?.Source==ScorebugColorSource.Custom;
    }

    /// <summary>Shows only the properties that mean something for this kind of element.</summary>
    void UpdatePropertyVisibility()
    {
        var e=Selected;
        var kind=e?.Kind;
        var text=kind==ScorebugElementKind.Text;
        TextSection.Visibility=text?Visibility.Visible:Visibility.Collapsed;
        FontSection.Visibility=kind is ScorebugElementKind.Text or ScorebugElementKind.Logo or ScorebugElementKind.Shape?Visibility.Visible:Visibility.Collapsed;
        LogoSection.Visibility=kind==ScorebugElementKind.Logo?Visibility.Visible:Visibility.Collapsed;
        ShapePanel.Visibility=kind==ScorebugElementKind.Shape?Visibility.Visible:Visibility.Collapsed;
        VideoPanel.Visibility=kind==ScorebugElementKind.Video?Visibility.Visible:Visibility.Collapsed;
        BackgroundSection.Visibility=kind is ScorebugElementKind.Text or ScorebugElementKind.Shape or ScorebugElementKind.Logo?Visibility.Visible:Visibility.Collapsed;
        PeriodSection.Visibility=text&&e!.Content.Contains("{{PERIOD}}",StringComparison.OrdinalIgnoreCase)?Visibility.Visible:Visibility.Collapsed;
        IconSection.Visibility=text?Visibility.Visible:Visibility.Collapsed;
        var icon=e?.Icon??ScorebugIcon.None;
        IconVisitorsInput.Visibility=icon==ScorebugIcon.TimeoutBars?Visibility.Visible:Visibility.Collapsed;
        IconFlipInput.Visibility=icon==ScorebugIcon.PossessionDot?Visibility.Visible:Visibility.Collapsed;
    }

    void Property_Changed(object sender,RoutedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,TextChangedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,SelectionChangedEventArgs e)=>ApplyProperties(sender);

    void ApplyProperties(object sender)
    {
        if(_loading||Selected is null)return;
        if(sender==ForegroundSourceInput||sender==BackgroundSourceInput){UpdateColorAvailability();SaveProperties();RefreshAllVisuals();return;}
        if(sender is ComboBox)
        {
            // Shape/font/animation changes need fresh geometry or a new face.
            SaveProperties(); Render(); return;
        }
        if(sender is CheckBox){SaveProperties();RefreshAllVisuals();return;}
        if(sender==ColorInput||sender==BackgroundInput){SaveProperties();RefreshAllVisuals();return;}
        if(sender==ContentInput) UpdatePropertyVisibility();
        _propertySave.Stop(); _propertySave.Start();
    }

    void SaveProperties()
    {
        if(_loading)return;
        var item=Selected; if(item is null)return;
        var name=NameInput.Text; var content=ContentInput.Text;
        var xOk=double.TryParse(XInput.Text,out var x); var yOk=double.TryParse(YInput.Text,out var y);
        var wOk=double.TryParse(WidthInput.Text,out var w); var hOk=double.TryParse(HeightInput.Text,out var h);
        var fOk=double.TryParse(FontInputSize.Text,out var f);
        var foregroundOk=TryColor(ColorInput.Text,out _); var backgroundOk=TryColor(BackgroundInput.Text,out _);
        var rotationOk=double.TryParse(RotationInput.Text,out var rotation);
        var borderWidthOk=double.TryParse(BorderWidthInput.Text,out var borderWidth);
        var borderColorOk=TryColor(BorderColorInput.Text,out _);
        var transitionSpeedOk=double.TryParse(TransitionSpeedInput.Text,out var transitionSpeed);
        var borderSource=(BorderSourceInput.SelectedItem as ColorSourceOption)?.Source??item.BorderSource;
        var animation=AnimationInput.SelectedItem is ScorebugAnimation a?a:item.Animation;
        var transition=TransitionInput.SelectedItem is ScorebugTransition t?t:item.Transition;
        var shape=ShapeInput.SelectedItem is ScorebugShape s?s:item.Shape;
        var visible=VisibleInput.IsChecked==true; var transparent=TransparentBackgroundInput.IsChecked==true;
        var mediaTag=MediaTagInput.Text.Trim();
        var preserveAspect=PreserveAspectInput.IsChecked==true;
        var aspectOk=double.TryParse(AspectInput.Text,out var aspect)&&aspect>0.05;
        var audioDevice=(AudioDeviceInput.SelectedItem as AudioOption)?.Id;
        // A preserved video box follows the picture's shape: moving one side moves the other.
        if(preserveAspect&&aspectOk&&wOk&&hOk)
        {
            var widthChanged=Math.Abs(w-item.Width)>0.5;
            var heightChanged=Math.Abs(h-item.Height)>0.5;
            if(heightChanged&&!widthChanged) w=h*aspect; else h=w/aspect;
        }
        var periodFormat=PeriodFormatInput.SelectedItem is PeriodOption period?period.Key:item.PeriodFormat;
        var icon=IconInput.SelectedItem is ScorebugIcon chosenIcon?chosenIcon:item.Icon;
        var iconVisitors=IconVisitorsInput.IsChecked==true; var iconFlipped=IconFlipInput.IsChecked==true;
        var font=(FontInput.SelectedItem as FontChoice)?.Source??item.FontFamily;
        var bold=BoldInput.IsChecked==true; var italic=ItalicInput.IsChecked==true;
        var underline=UnderlineInput.IsChecked==true; var strike=StrikeInput.IsChecked==true;
        var foregroundSource=(ForegroundSourceInput.SelectedItem as ColorSourceOption)?.Source??item.ForegroundSource;
        var backgroundSource=(BackgroundSourceInput.SelectedItem as ColorSourceOption)?.Source??item.BackgroundSource;
        var logoSide=(LogoInput.SelectedItem as LogoOption)?.Side??item.LogoSide;
        if(item.Name==name&&item.Content==content&&(!xOk||item.X==x)&&(!yOk||item.Y==y)            &&(!wOk||item.Width==w)&&(!hOk||item.Height==h)
            &&item.PreserveVideoAspect==preserveAspect&&(!aspectOk||Math.Abs(item.VideoAspect-aspect)<0.001)
            &&string.Equals(item.AudioDevice,audioDevice,StringComparison.Ordinal)
            &&(!fOk||item.FontSize==f)&&(!foregroundOk||item.Foreground==ColorInput.Text)&&(!backgroundOk||item.Background==BackgroundInput.Text)
            &&item.Animation==animation&&item.Transition==transition&&item.Shape==shape&&item.Visible==visible&&item.TransparentBackground==transparent
            &&item.MediaTag==mediaTag&&item.PeriodFormat==periodFormat
            &&item.Icon==icon&&item.IconVisitors==iconVisitors&&item.IconFlipped==iconFlipped
            &&item.FontFamily==font&&item.Bold==bold&&item.Italic==italic&&item.Underline==underline&&item.StrikeThrough==strike
            &&item.ForegroundSource==foregroundSource&&item.BackgroundSource==backgroundSource&&item.LogoSide==logoSide
            &&(!rotationOk||Math.Abs(item.Rotation-rotation)<0.001)&&(!borderWidthOk||Math.Abs(item.BorderThickness-borderWidth)<0.001)
            &&(!borderColorOk||item.BorderColor==BorderColorInput.Text)&&item.BorderSource==borderSource
            &&(!transitionSpeedOk||Math.Abs(item.TransitionSeconds-transitionSpeed)<0.001))return;
        var id=item.Id;
        // The save raises Changed, which would rebuild the canvas and restart every video; this
        // edit paints in place through RefreshAllVisuals instead.
        _skipRebuild=true;
        try
        {
        _catalog.Update(layout=>{
            var target=layout.Elements.FirstOrDefault(z=>z.Id==id); if(target is null)return;
            target.Name=name; target.Content=content;
            if(xOk)target.X=x; if(yOk)target.Y=y; if(wOk)target.Width=w; if(hOk)target.Height=h;
            if(fOk)target.FontSize=f;
            if(foregroundOk)target.Foreground=ColorInput.Text;
            if(backgroundOk)target.Background=BackgroundInput.Text;
            target.Animation=animation; target.Transition=transition; target.Shape=shape;
            target.Visible=visible; target.TransparentBackground=transparent;
            target.MediaTag=mediaTag; target.PeriodFormat=periodFormat;
            target.PreserveVideoAspect=preserveAspect;
            if(aspectOk)target.VideoAspect=Math.Clamp(aspect,0.1,10);
            target.AudioDevice=string.IsNullOrWhiteSpace(audioDevice)?null:audioDevice;
            target.Icon=icon; target.IconVisitors=iconVisitors; target.IconFlipped=iconFlipped;
            target.FontFamily=font; target.Bold=bold; target.Italic=italic;
            target.Underline=underline; target.StrikeThrough=strike;
            target.ForegroundSource=foregroundSource; target.BackgroundSource=backgroundSource;
            target.LogoSide=logoSide;
            if(rotationOk)target.Rotation=Math.Clamp(rotation,-360,360);
            if(borderWidthOk)target.BorderThickness=Math.Clamp(borderWidth,0,40);
            if(borderColorOk)target.BorderColor=BorderColorInput.Text;
            target.BorderSource=borderSource;
            if(transitionSpeedOk)target.TransitionSeconds=Math.Clamp(transitionSpeed,0,5);
            if(target.Kind==ScorebugElementKind.Logo&&target.LogoSide==ScorebugLogoSide.None)target.LogoSide=ScorebugLogoSide.Home;
        });
        }
        finally { _skipRebuild=false; }
    }

    static bool TryColor(string value,out Color color){try{color=(Color)ColorConverter.ConvertFromString(value)!;return true;}catch{color=Colors.White;return false;}}

    // ---------- fonts ----------

    void ImportFont_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="Import a font",Filter="Fonts (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc|All files (*.*)|*.*"};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var source=FontCatalog.Import(dialog.FileName);
            FontInput.ItemsSource=FontCatalog.All;
            var choice=FontCatalog.ChoiceFor(source);
            FontInput.SelectedItem=FontCatalog.All.FirstOrDefault(c=>string.Equals(c.Source,choice.Source,StringComparison.OrdinalIgnoreCase))??choice;
            SaveProperties();
            Render();
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,$"Could not import that font: {ex.Message}","Scorebug Studio",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
    }

    // ---------- displays ----------

    void DisplayBox_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_displayLoading||DisplayBox.SelectedItem is not DisplayOption option)return;
        var index=_catalog.Displays.FindIndex(display=>string.Equals(display.Id,option.Id,StringComparison.OrdinalIgnoreCase));
        if(index<0||index==_catalog.ActiveDisplayIndex)return;
        ClearSelection();
        _catalog.SelectDisplay(index);
        Fit_Click(this,new RoutedEventArgs());
    }

    void DisplayNew_Click(object sender,RoutedEventArgs e)
    {
        var name=PromptText("New display","Name this display. It starts with one blank layout and gets its own audience window.",$"Display {_catalog.Displays.Count+1}");
        if(string.IsNullOrWhiteSpace(name))return;
        ClearSelection();
        _catalog.AddDisplay(name);
        Render();
        Fit_Click(this,new RoutedEventArgs());
    }

    void DisplayRename_Click(object sender,RoutedEventArgs e)
    {
        var name=PromptText("Rename display","Display name",_catalog.ActiveDisplay.Name);
        if(string.IsNullOrWhiteSpace(name))return;
        _catalog.RenameDisplay(_catalog.ActiveDisplayIndex,name);
        Render();
    }

    void DisplayDelete_Click(object sender,RoutedEventArgs e)
    {
        if(_catalog.Displays.Count<=1)
        {
            MessageBox.Show(this,"A game needs at least one display.","Scorebug Studio",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        if(MessageBox.Show(this,$"Delete the display \"{_catalog.ActiveDisplay.Name}\" and its {_catalog.Layouts.Count} layout(s)?","Scorebug Studio",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        ClearSelection();
        _catalog.RemoveDisplay(_catalog.ActiveDisplayIndex);
        Render();
    }

    /// <summary>Choosing another display only re-aims the link editor; it never writes a link yet.</summary>
    void LinkDisplay_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_linkLoading)return;
        var option=LinkDisplayBox.SelectedItem as LinkDisplayOption;
        _linkLoading=true;
        try { FillLinkLayouts(option?.Id,null); }
        finally { _linkLoading=false; }
        // Clearing the target clears the link, including the coming-back half; picking a layout
        // below sets it again.
        if(option?.Id is null)
        {
            ApplyReciprocal(false);
            _catalog.SetLink(_catalog.ActiveDisplay.Id,_catalog.ActiveIndex,null,null);
        }
    }

    void LinkLayout_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_linkLoading)return;
        if(LinkLayoutBox.SelectedItem is not LinkLayoutOption layout||layout.Id is null)return;
        if(LinkDisplayBox.SelectedItem is not LinkDisplayOption display||display.Id is null)return;
        // Read the tick first: clearing the old pair reloads the panel, which would reset it.
        var bothWays=LinkBothWaysInput.IsChecked==true;
        // A fresh target means any previous coming-back link belongs to the old pair.
        ApplyReciprocal(false);
        _catalog.SetLink(_catalog.ActiveDisplay.Id,_catalog.ActiveIndex,display.Id,layout.Id);
        if(bothWays)ApplyReciprocal(true);
    }

    void LinkClear_Click(object sender,RoutedEventArgs e)
    {
        ApplyReciprocal(false);
        _catalog.SetLink(_catalog.ActiveDisplay.Id,_catalog.ActiveIndex,null,null);
        Render();
    }

    /// <summary>A small themed name prompt, used by the display buttons.</summary>
    string? PromptText(string title,string hint,string initial)
    {
        var dialog=new Window{Title=title,Width=400,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
        var panel=new StackPanel{Margin=new Thickness(16)};
        panel.Children.Add(new TextBlock{Text=hint,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,2,8),Opacity=0.8});
        var input=new TextBox{Text=initial,Margin=new Thickness(2),FontSize=13};
        panel.Children.Add(input);
        var ok=new Button{Content="Save",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(2,10,2,0),MinHeight=34,Padding=new Thickness(14,6,14,6)};
        string? result=null;
        ok.Click+=(_,_)=>{result=input.Text.Trim();dialog.DialogResult=true;dialog.Close();};
        panel.Children.Add(ok);
        dialog.Content=panel;
        dialog.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};
        ThemeManager.Apply(dialog,_theme);
        return dialog.ShowDialog()==true&&!string.IsNullOrWhiteSpace(result)?result:null;
    }

    // ---------- layouts ----------

    void LayoutBox_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_layoutLoading||LayoutBox.SelectedIndex<0)return;
        if(LayoutBox.SelectedIndex==_catalog.ActiveIndex)return;
        ClearSelection();
        _catalog.SelectLayout(LayoutBox.SelectedIndex);
    }

    void LayoutNew_Click(object sender,RoutedEventArgs e)
    {
        _catalog.AddLayout($"Layout {_catalog.Layouts.Count+1}");
        ClearSelection();
        Render();
    }

    void LayoutDuplicate_Click(object sender,RoutedEventArgs e)
    {
        _catalog.AddLayout($"{_catalog.Layout.Name} copy",copyActive:true);
        ClearSelection();
        Render();
    }

    void LayoutDelete_Click(object sender,RoutedEventArgs e)
    {
        if(_catalog.Layouts.Count<=1)
        {
            MessageBox.Show(this,"A game needs at least one scorebug layout.","Scorebug Studio",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        if(MessageBox.Show(this,$"Delete the layout \"{_catalog.Layout.Name}\"?","Scorebug Studio",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        ClearSelection();
        _catalog.RemoveLayout(_catalog.ActiveIndex);
        Render();
    }

    void LayoutRename_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new Window{Title="Rename layout",Width=380,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(16)};
        panel.Children.Add(new TextBlock{Text="Layout name",Margin=new Thickness(4,0,4,6)});
        var input=new TextBox{Text=_catalog.Layout.Name,Margin=new Thickness(4)};
        panel.Children.Add(input);
        var ok=new Button{Content="Rename",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(4,10,4,0)};
        ok.Click+=(_,_)=>{_catalog.RenameLayout(_catalog.ActiveIndex,input.Text);dialog.DialogResult=true;dialog.Close();};
        panel.Children.Add(ok);
        dialog.Content=panel; ThemeManager.Apply(dialog,_theme);
        dialog.ShowDialog();
        Render();
    }

    // New artwork starts with no background box so it lands clean on the canvas; turn one on in the properties if needed.
    void AddText_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New text",Content="EDIT TEXT",X=480,Y=470,TransparentBackground=true});
    void AddLogo_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New logo",Kind=ScorebugElementKind.Logo,LogoSide=ScorebugLogoSide.Home,Content="HOME LOGO",X=520,Y=470,Width=140,Height=90,TransparentBackground=true});
    void AddShape_Click(object sender,RoutedEventArgs e)
    {
        var picker=new Window{Title="Add shape",Width=340,Height=190,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(14)};
        panel.Children.Add(new TextBlock{Text="Shape",Margin=new Thickness(4)});
        var shape=new ComboBox{ItemsSource=Enum.GetValues<ScorebugShape>(),SelectedItem=ScorebugShape.Rectangle,Margin=new Thickness(4)};
        var add=new Button{Content="Add shape",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right};
        add.Click+=(_,_)=>{
            var kind=(ScorebugShape)(shape.SelectedItem??ScorebugShape.Rectangle);
            Add(new ScorebugElement{Name=$"New {kind.ToString().ToLowerInvariant()}",Kind=ScorebugElementKind.Shape,Shape=kind,Content="",X=450,Y=470,Width=kind==ScorebugShape.Rectangle?200:140,Height=kind==ScorebugShape.Rectangle?100:140,TransparentBackground=true,BorderThickness=2});
            picker.DialogResult=true; picker.Close();
        };
        panel.Children.Add(shape); panel.Children.Add(add); picker.Content=panel; ThemeManager.Apply(picker,_theme); picker.ShowDialog();
    }

    void AddData_Click(object sender,RoutedEventArgs e)
    {
        // Every token the binder understands, so no data is missing from the studio.
        var choices=new List<(string,string)>
        {
            ("Team · home name","{{HOME_TEAM}}"),("Team · visitors name","{{AWAY_TEAM}}"),
            ("Team · home abbreviation","{{HOME_ABBR}}"),("Team · visitors abbreviation","{{AWAY_ABBR}}"),
            ("Score · home","{{HOME_SCORE}}"),("Score · visitors","{{AWAY_SCORE}}"),
            ("Period (quarter)","{{PERIOD}}"),("Game clock","{{GAME_CLOCK}}"),("Play clock","{{PLAY_CLOCK}}"),
            ("Timeout clock","{{TIMEOUT_CLOCK}}"),("Timeouts left","{{TIMEOUTS}}"),("Possession","{{POSSESSION}}"),
            ("Sport name","{{SPORT}}"),("Date","{{DATE}}"),("Time of day","{{TIME}}"),
            ("Football · down","{{DOWN}}"),("Football · to go","{{TO_GO}}"),("Football · down & distance","{{DOWNS}}"),
            ("Football · yards to go","{{YARDS_TO_GO}}"),("Football · ball on (number)","{{BALL_ON}}"),
            ("Football · ball on side","{{BALL_SIDE}}"),("Football · ball on side + number","{{BALL_SIDE_SPOT}}"),
            ("Football · ball side team name","{{BALL_TEAM}}"),("Football · ball spot","{{BALL_SPOT}}"),
            ("Football · team yards","{{YARDS}}"),("Football · home yards","{{HOME_YARDS}}"),("Football · visitors yards","{{AWAY_YARDS}}"),
            ("Football · turnovers","{{TURNOVERS}}"),
            ("Basketball · shot clock","{{SHOT_CLOCK}}"),("Basketball · fouls","{{BASKETBALL_FOULS}}"),("Fouls (both)","{{FOULS}}"),
            ("Baseball · count & outs","{{BASEBALL_COUNT}}"),("Baseball · hits","{{HITS}}"),("Baseball · outs","{{BASEBALL_OUTS}}"),
            ("Soccer · cards","{{SOCCER_CARDS}}"),("Soccer · cards (short)","{{CARDS}}"),("Soccer · shots","{{SHOTS}}"),("Soccer · corners","{{CORNERS}}")
        };
        foreach(var stat in _engine.State.Stats)choices.Add(($"Custom stat · {stat.Name}",$"{{{{STAT:{stat.Name}}}}}"));
        choices.Add(("Custom stat · any name","{{STAT:Name}}"));
        var picker=new Window{Title="Add sport data",Width=380,Height=470,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var list=new ListBox{ItemsSource=choices.Select(x=>x.Item1).ToArray(),Margin=new Thickness(10)};
        var add=new Button{Content="Add selected data",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right};            add.Click+=(_,_)=>{if(list.SelectedIndex<0)return;var token=choices[list.SelectedIndex].Item2;Add(new ScorebugElement{Name=choices[list.SelectedIndex].Item1,Content=token,X=420,Y=470,TransparentBackground=true});picker.DialogResult=true;picker.Close();};
        picker.Content=new DockPanel{Children={add,list}}; DockPanel.SetDock(add,Dock.Bottom); ThemeManager.Apply(picker,_theme); picker.ShowDialog();
    }

    /// <summary>A video box that only shows clips carrying its tag.</summary>
    void AddVideo_Click(object sender,RoutedEventArgs e)
    {
        var picker=new Window{Title="Add video element",Width=380,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(23,28,37)),Foreground=Brushes.White};
        var panel=new StackPanel{Margin=new Thickness(14)};
        panel.Children.Add(new TextBlock{Text="Which media tag should play inside this element?",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,0,4,8)});
        var tag=new ComboBox{IsEditable=true,Text=MediaLibrary.AnyTag,ItemsSource=MediaLibrary.SuggestedTags.ToArray(),Margin=new Thickness(4)};
        panel.Children.Add(tag);
        panel.Children.Add(new TextBlock{Text="ANY plays whatever clip is cued, whatever tag it carries. Name a tag (Ads, Highlights…) to show only that tag's clips.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(4,6,4,0)});
        var add=new Button{Content="Add video box",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(4,12,4,0)};
        add.Click+=(_,_)=>{
            var width=Math.Round(_catalog.Layout.Width*0.36); var height=Math.Round(width*9/16);
            Add(new ScorebugElement{
                Name=$"Video · {MediaLibrary.NormalizeTag(tag.Text)}",Kind=ScorebugElementKind.Video,Content="",
                MediaTag=MediaLibrary.NormalizeTag(tag.Text),
                X=Math.Round((_catalog.Layout.Width-width)/2),Y=Math.Round((_catalog.Layout.Height-height)/2),
                Width=width,Height=height,TransparentBackground=true});
            picker.DialogResult=true; picker.Close();
        };
        panel.Children.Add(add); picker.Content=panel; ThemeManager.Apply(picker,_theme); picker.ShowDialog();
    }

    static int PresetIndex(int width,int height)
    {
        var index=Array.FindIndex(ScorebugLayout.SizePresets,p=>p.Width==width&&p.Height==height);
        return index<0?0:index;
    }

    void CanvasPreset_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_canvasLoading||CanvasPreset.SelectedIndex<0)return;
        var preset=ScorebugLayout.SizePresets[CanvasPreset.SelectedIndex];
        ApplyCanvasSize(preset.Width,preset.Height);
    }

    void CanvasResize_Click(object sender,RoutedEventArgs e)
    {
        var width=int.TryParse(CanvasWidthInput.Text,out var w)?w:_catalog.Layout.Width;
        var height=int.TryParse(CanvasHeightInput.Text,out var h)?h:_catalog.Layout.Height;
        ApplyCanvasSize(width,height);
    }

    /// <summary>Changing the canvas size scales every element with it, so a 720p layout survives a move to 4K.</summary>
    void ApplyCanvasSize(int width,int height)
    {
        if(width<320||height<240)return;
        _catalog.Resize(width,height);
        Render();
        Fit_Click(this,new RoutedEventArgs());
    }

    void Add(ScorebugElement item)
    {
        _catalog.Update(layout=>layout.Elements.Add(item));
        ClearSelection();
        _selectedIds.Add(item.Id);
        _selectedId=item.Id;
        Render();
    }

    /// <summary>Deletes every selected object at once — the Remove button and the Delete key.</summary>
    void RemoveSelected()
    {
        if(_selectedIds.Count==0)return;
        var ids=_selectedIds.ToArray();
        ClearSelection();
        _catalog.Update(layout=>layout.Elements.RemoveAll(x=>ids.Contains(x.Id)));
        Render();
    }

    void Remove_Click(object sender,RoutedEventArgs e) => RemoveSelected();

    void Element_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(_resizing||sender is not Border host||host.Tag is not ScorebugElement item)return;
        var id=item.Id;
        if((Keyboard.Modifiers&ModifierKeys.Control)!=0)
        {
            // Ctrl-click adds this object to the selection, or takes it back out again.
            if(_selectedIds.Contains(id))
            {
                _selectedIds.Remove(id);
                if(_selectedId==id)_selectedId=_selectedIds.LastOrDefault();
            }
            else
            {
                _selectedIds.Add(id);
                _selectedId=id;
            }
        }
        else if(!_selectedIds.Contains(id))
        {
            _selectedIds.Clear();
            _selectedIds.Add(id);
            _selectedId=id;
        }
        else _selectedId=id; // dragging one of several keeps the whole group together
        SyncLayersFromSelection();
        RefreshAllVisuals();
        LoadProperties();
        if(_selectedIds.Count==0||_selectedId!=id)return;
        _dragElement=item;
        _dragStartPoint=e.GetPosition(DesignCanvas);
        _dragOrigins.Clear();
        foreach(var selectedId in _selectedIds)
        {
            var element=_catalog.Layout.Elements.FirstOrDefault(x=>x.Id==selectedId);
            if(element is not null)_dragOrigins[selectedId]=(element.X,element.Y);
        }
        host.CaptureMouse();
        e.Handled=true;
    }

    void Element_MouseMove(object sender,MouseEventArgs e)
    {
        if(_resizing||_dragElement is null||e.LeftButton!=MouseButtonState.Pressed)return;
        var p=e.GetPosition(DesignCanvas);
        var dx=p.X-_dragStartPoint.X;
        var dy=p.Y-_dragStartPoint.Y;
        // The whole selection moves by the same offset, each box still kept inside the artwork.
        foreach(var (selectedId,origin) in _dragOrigins)
        {
            var element=_catalog.Layout.Elements.FirstOrDefault(x=>x.Id==selectedId);
            if(element is null)continue;
            var x=Math.Clamp(origin.X+dx,0,Math.Max(0,_catalog.Layout.Width-element.Width));
            var y=Math.Clamp(origin.Y+dy,0,Math.Max(0,_catalog.Layout.Height-element.Height));
            element.X=x; element.Y=y;
            if(_visuals.TryGetValue(selectedId,out var visual)){Canvas.SetLeft(visual.Host,x);Canvas.SetTop(visual.Host,y);}
        }
        if(_dragElement is { } primary&&!string.IsNullOrEmpty(primary.Id))
        {
            var current=_catalog.Layout.Elements.FirstOrDefault(x=>x.Id==primary.Id);
            if(current is not null&&!_loading){_loading=true;XInput.Text=current.X.ToString("0");YInput.Text=current.Y.ToString("0");_loading=false;}
        }
    }

    void Element_MouseUp(object sender,MouseButtonEventArgs e)
    {
        if(_resizing)return;
        if(sender is Border host)host.ReleaseMouseCapture();
        SaveDrag();
        e.Handled=true;
    }

    /// <summary>Writes every dragged object's new position in one catalog edit.</summary>
    void SaveDrag()
    {
        if(_dragElement is null)return;
        _dragElement=null;
        var moved=_catalog.Layout.Elements
            .Where(x=>_dragOrigins.ContainsKey(x.Id))
            .ToDictionary(x=>x.Id,x=>(x.X,x.Y));
        _dragOrigins.Clear();
        if(moved.Count==0)return;
        _catalog.Update(layout=>
        {
            foreach(var (id,position) in moved)
            {
                var target=layout.Elements.FirstOrDefault(z=>z.Id==id);
                if(target is not null){target.X=position.X;target.Y=position.Y;}
            }
        });
    }

    void Canvas_MouseMove(object sender,MouseEventArgs e) { }

    void Canvas_MouseUp(object sender,MouseButtonEventArgs e)
    {
        if(_resizing)return;
        SaveDrag();
    }

    void Resize_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(sender is not Border handle||handle.Tag is not ScorebugElement item)return;
        _dragElement=item; _resizing=true;
        _selectedIds.Clear(); _selectedIds.Add(item.Id); _selectedId=item.Id;
        SyncLayersFromSelection();
        RefreshAllVisuals();
        LoadProperties();
        var origin=e.GetPosition(DesignCanvas);
        _resizeStartX=origin.X; _resizeStartY=origin.Y;
        _resizeStartWidth=item.Width; _resizeStartHeight=item.Height;
        handle.CaptureMouse();
        e.Handled=true;
    }

    void Resize_MouseMove(object sender,MouseEventArgs e)
    {
        if(_dragElement is null||!_resizing||e.LeftButton!=MouseButtonState.Pressed)return;
        var p=e.GetPosition(DesignCanvas);
        var width=Math.Clamp(_resizeStartWidth+(p.X-_resizeStartX),24,Math.Max(24,_catalog.Layout.Width-_dragElement.X));
        var height=Math.Clamp(_resizeStartHeight+(p.Y-_resizeStartY),24,Math.Max(24,_catalog.Layout.Height-_dragElement.Y));
        // A video box that preserves its aspect ratio can only be resized along one diagonal.
        if(_dragElement.PreserveVideoAspect&&_dragElement.VideoAspect>0.05)
        {
            height=Math.Clamp(width/_dragElement.VideoAspect,24,Math.Max(24,_catalog.Layout.Height-_dragElement.Y));
            width=height*_dragElement.VideoAspect;
        }
        _dragElement.Width=width; _dragElement.Height=height;
        if(_visuals.TryGetValue(_dragElement.Id,out var visual))
        {
            visual.Host.Width=width; visual.Host.Height=height;
            visual.Content.Width=double.NaN; visual.Content.Height=double.NaN;
        }
        if(!_loading){_loading=true;WidthInput.Text=width.ToString("0");HeightInput.Text=height.ToString("0");_loading=false;}
        e.Handled=true;
    }

    void Resize_MouseUp(object sender,MouseButtonEventArgs e)
    {
        if(sender is Border handle)handle.ReleaseMouseCapture();
        if(_dragElement is { } item)
        {
            var id=item.Id; var w=item.Width; var h=item.Height;
            _dragElement=null; _resizing=false;
            _catalog.Update(layout=>{var target=layout.Elements.FirstOrDefault(z=>z.Id==id);if(target is not null){target.Width=Math.Clamp(w,24,layout.Width);target.Height=Math.Clamp(h,24,layout.Height);}});
        }
        _resizing=false;
        e.Handled=true;
    }

    void Layers_MouseDown(object sender,MouseButtonEventArgs e)=>_listStart=e.GetPosition(null);

    void Layers_MouseMove(object sender,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed||Layers.SelectedItem is not LayerItem item)return;
        var p=e.GetPosition(null);
        if(Math.Abs(p.X-_listStart.X)>8||Math.Abs(p.Y-_listStart.Y)>8)DragDrop.DoDragDrop(Layers,item.Element.Id,DragDropEffects.Move);
    }

    void Layers_Drop(object sender,DragEventArgs e)
    {
        if(e.Data.GetData(typeof(string)) is not string movedId)return;
        var p=e.GetPosition(Layers);
        var target=Layers.InputHitTest(p) as DependencyObject;
        while(target is not null&&target is not ListBoxItem)target=VisualTreeHelper.GetParent(target);
        if(target is not ListBoxItem row||row.Content is not LayerItem drop)return;
        var from=_catalog.Layout.Elements.FindIndex(x=>x.Id==movedId);
        var to=_catalog.Layout.Elements.FindIndex(x=>x.Id==drop.Element.Id);
        if(from<0||to<0||from==to)return;
        _catalog.Update(layout=>{var item=layout.Elements[from];layout.Elements.RemoveAt(from);layout.Elements.Insert(Math.Clamp(to,0,layout.Elements.Count),item);});
    }

    void ChoosePresetColor(TextBox target)
    {
        var picked=ColorPickerWindow.Pick(this,target.Text);
        if(picked is not null){target.Text=picked;SaveProperties();RefreshAllVisuals();}
    }

    void TextColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(ColorInput);
    void BackgroundColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(BackgroundInput);
    void BorderColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(BorderColorInput);
    void Fit_Click(object sender,RoutedEventArgs e)
    {
        if(CanvasFrame.ActualWidth<10||CanvasFrame.ActualHeight<10)return;
        var scale=Math.Min((CanvasFrame.ActualWidth-8)/_catalog.Layout.Width,(CanvasFrame.ActualHeight-8)/_catalog.Layout.Height);
        CanvasScale.ScaleX=CanvasScale.ScaleY=Math.Clamp(scale,.2,1);
    }
    void Done_Click(object sender,RoutedEventArgs e)=>Close();

    // Enter inside a property field commits that value. It must never close the studio.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Enter&&(Keyboard.FocusedElement is TextBox||e.OriginalSource is TextBox))
        {
            _propertySave.Stop(); SaveProperties(); RefreshAllVisuals();
            e.Handled=true;
            return;
        }
        // Delete removes every selected object, but never while the operator is typing a value.
        if(e.Key==Key.Delete&&Keyboard.FocusedElement is not TextBox&&e.OriginalSource is not TextBox)
        {
            if(_selectedIds.Count>0)
            {
                RemoveSelected();
                e.Handled=true;
                return;
            }
        }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        _propertySave.Stop(); SaveProperties();
        foreach(var surface in _videoSurfaces.Values) surface.Dispose();
        _videoSurfaces.Clear();
        _wallClock.Stop();
        _catalog.Changed-=Render; _engine.Changed-=RefreshBoundText;
        base.OnClosed(e);
    }
    /// <summary>Moves the selected element one step towards the front (+1) or back (-1) of the picture.</summary>
    void MoveSelectedLayer(int delta)
    {
        if(Selected is not { } item) return;
        var from=_catalog.Layout.Elements.IndexOf(item);
        if(from<0) return;
        var to=Math.Clamp(from+delta,0,_catalog.Layout.Elements.Count-1);
        if(from==to) return;
        _catalog.Update(layout=>{var moved=layout.Elements[from];layout.Elements.RemoveAt(from);layout.Elements.Insert(to,moved);});
    }

    void LayerFront_Click(object sender,RoutedEventArgs e)=>MoveSelectedLayer(1);
    void LayerBack_Click(object sender,RoutedEventArgs e)=>MoveSelectedLayer(-1);

    sealed class LayerItem
    {
        public LayerItem(ScorebugElement element)=>Element=element;
        public ScorebugElement Element { get; }
        public string Glyph => !Element.Visible ? "○" : Element.Kind switch
        {
            ScorebugElementKind.Logo => "▣",
            ScorebugElementKind.Shape => "◼",
            ScorebugElementKind.Video => "▶",
            _ => "T"
        };
        public string Label => Element.Name;
        public string KindLabel => Element.Kind.ToString().ToUpperInvariant();
    }
}
