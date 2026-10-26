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
    readonly List<VideoSurface> _videoSurfaces = [];
    bool _canvasLoading;
    readonly System.Windows.Threading.DispatcherTimer _propertySave = new() { Interval=TimeSpan.FromMilliseconds(350) };
    readonly Dictionary<string,(Border Host, FrameworkElement Content, Border Handle)> _visuals = [];
    readonly Dictionary<string,(ScorebugElementKind Kind, ScorebugShape Shape, string? Image, ScorebugAnimation Animation, double Opacity)> _applied = [];
    bool _loading;
    bool _syncingLayers;
    string? _selectedId;
    ScorebugElement? _dragElement;
    bool _resizing;
    double _resizeStartX, _resizeStartY, _resizeStartWidth, _resizeStartHeight;
    Point _dragOffset;
    Point _listStart;

    public ScorebugEditorWindow(ScorebugCatalog catalog, GameEngine engine, MediaPlayback? playback=null, string theme="Dark")
    {
        InitializeComponent(); _catalog=catalog; _engine=engine; _playback=playback; _theme=theme;
        ThemeManager.Apply(this,theme);
        AnimationInput.ItemsSource=Enum.GetValues<ScorebugAnimation>();
        ShapeInput.ItemsSource=Enum.GetValues<ScorebugShape>();
        CanvasPreset.ItemsSource=ScorebugLayout.SizePresets.Select(p=>p.Name).ToArray();
        _canvasLoading=true;
        CanvasPreset.SelectedIndex=PresetIndex(catalog.Layout.Width,catalog.Layout.Height);
        CanvasWidthInput.Text=catalog.Layout.Width.ToString();
        CanvasHeightInput.Text=catalog.Layout.Height.ToString();
        _canvasLoading=false;
        _propertySave.Tick+=(_,_)=>{_propertySave.Stop();SaveProperties();RefreshAllVisuals();};
        _catalog.Changed += Render;
        _engine.Changed += RefreshBoundText;
        Render();
    }

    ScorebugElement? Selected => _selectedId is null ? null : _catalog.Layout.Elements.FirstOrDefault(x=>x.Id==_selectedId);

    void Render()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Render);return;}
        if(_dragElement is not null)return; // never rebuild under an active gesture
        if(_selectedId is not null && _catalog.Layout.Elements.All(x=>x.Id!=_selectedId))_selectedId=null;
        _loading=true;
        _syncingLayers=true;
        Layers.ItemsSource=_catalog.Layout.Elements.Select(e=>new LayerItem(e)).ToArray();
        Layers.SelectedItem=Layers.Items.Cast<LayerItem>().FirstOrDefault(x=>x.Element.Id==_selectedId);
        _syncingLayers=false;
        foreach(var surface in _videoSurfaces) surface.Dispose();
        _videoSurfaces.Clear();
        DesignCanvas.Width=_catalog.Layout.Width; DesignCanvas.Height=_catalog.Layout.Height;
        // Elements (video especially) must never paint outside the artwork they belong to.
        DesignCanvas.ClipToBounds=true;
        CanvasInfo.Text=$"{_catalog.Layout.Width} × {_catalog.Layout.Height} · drag elements to reposition · bindings update live";
        if(!_canvasLoading)
        {
            _canvasLoading=true;
            CanvasWidthInput.Text=_catalog.Layout.Width.ToString();
            CanvasHeightInput.Text=_catalog.Layout.Height.ToString();
            CanvasPreset.SelectedIndex=PresetIndex(_catalog.Layout.Width,_catalog.Layout.Height);
            _canvasLoading=false;
        }
        DesignCanvas.Children.Clear(); _visuals.Clear(); _applied.Clear();
        foreach(var item in _catalog.Layout.Elements) DesignCanvas.Children.Add(CreateVisual(item));
        _loading=false;
        LoadProperties();
    }

    Border CreateVisual(ScorebugElement item)
    {
        var content=ScorebugRender.CreateContent(item,()=>_engine.State,_playback,true);
        if(content is VideoSurface videoSurface)_videoSurfaces.Add(videoSurface);
        var handle=new Border
        {
            Width=16, Height=16, Background=Brushes.DeepSkyBlue, BorderBrush=Brushes.White,
            BorderThickness=new Thickness(1), Cursor=Cursors.SizeNWSE, Tag=item,
            HorizontalAlignment=HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Bottom,
            Margin=new Thickness(0,0,1,1),
            Visibility=item.Id==_selectedId?Visibility.Visible:Visibility.Collapsed,
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
            Width=item.Width, Height=item.Height, Background=ScorebugRender.HostBackground(item),
            ClipToBounds=true,
            Opacity=item.Opacity, Cursor=Cursors.SizeAll, Tag=item, Child=stack,
            BorderBrush=item.Id==_selectedId?Brushes.DeepSkyBlue:Brushes.Transparent,
            BorderThickness=new Thickness(item.Id==_selectedId?2:1)
        };
        Canvas.SetLeft(host,item.X); Canvas.SetTop(host,item.Y); Panel.SetZIndex(host,_catalog.Layout.Elements.IndexOf(item));
        host.MouseLeftButtonDown+=Element_MouseDown; host.MouseMove+=Element_MouseMove; host.MouseLeftButtonUp+=Element_MouseUp;
        _visuals[item.Id]=(host,content,handle);
        ApplyAnimation(item,host);
        return host;
    }

    void ApplyAnimation(ScorebugElement item,FrameworkElement host)
    {
        if(_applied.TryGetValue(item.Id,out var applied)&&applied.Animation==item.Animation&&Math.Abs(applied.Opacity-item.Opacity)<0.0001)return;
        host.BeginAnimation(UIElement.OpacityProperty,null);
        if(host.RenderTransform is TranslateTransform previous){previous.BeginAnimation(TranslateTransform.XProperty,null);host.RenderTransform=Transform.Identity;}
        switch(item.Animation)
        {
            case ScorebugAnimation.Pulse:
                host.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(item.Opacity*0.72,item.Opacity,TimeSpan.FromSeconds(1.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
                break;
            case ScorebugAnimation.Drift:
                host.RenderTransform=new TranslateTransform();
                host.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,10,TimeSpan.FromSeconds(2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
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
            if(visual.Content is TextBlock)
                ScorebugRender.ApplyToContent(visual.Content,item,ScoreboardTemplates.Bind(item.Content,_engine.State));
            else if(visual.Content is not VideoSurface)
                ScorebugRender.ApplyToContent(visual.Content,item);
            visual.Host.Width=item.Width; visual.Host.Height=item.Height;
            visual.Host.Background=ScorebugRender.HostBackground(item);
            if(item.Animation!=ScorebugAnimation.Pulse)visual.Host.Opacity=item.Opacity;
            Canvas.SetLeft(visual.Host,item.X); Canvas.SetTop(visual.Host,item.Y);
            visual.Handle.Visibility=item.Id==_selectedId?Visibility.Visible:Visibility.Collapsed;
            visual.Host.BorderBrush=item.Id==_selectedId?Brushes.DeepSkyBlue:Brushes.Transparent;
            visual.Host.BorderThickness=new Thickness(item.Id==_selectedId?2:1);
            ApplyAnimation(item,visual.Host);
        }
    }

    void RefreshBoundText()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(RefreshBoundText);return;}
        if(_dragElement is not null)return;
        foreach(var item in _catalog.Layout.Elements)
            if(item.Kind==ScorebugElementKind.Text&&_visuals.TryGetValue(item.Id,out var visual)&&visual.Content is TextBlock block)
                block.Text=ScoreboardTemplates.Bind(item.Content,_engine.State);
    }

    void Layers_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_syncingLayers)return;
        var id=(Layers.SelectedItem as LayerItem)?.Element.Id;
        if(id==_selectedId)return;
        _selectedId=id;
        RefreshAllVisuals();
        LoadProperties();
    }

    void LoadProperties()
    {
        _loading=true;
        var e=Selected; var has=e is not null;
        foreach(var control in new Control[]{NameInput,ContentInput,XInput,YInput,WidthInput,HeightInput,FontInput,ColorInput,BackgroundInput,AnimationInput,ShapeInput,VisibleInput,TransparentBackgroundInput,MediaTagInput})
            control.IsEnabled=has;
        ShapePanel.Visibility=e?.Kind==ScorebugElementKind.Shape?Visibility.Visible:Visibility.Collapsed;
        VideoPanel.Visibility=e?.Kind==ScorebugElementKind.Video?Visibility.Visible:Visibility.Collapsed;
        if(has)
        {
            NameInput.Text=e!.Name; ContentInput.Text=e.Content; XInput.Text=e.X.ToString("0"); YInput.Text=e.Y.ToString("0");
            WidthInput.Text=e.Width.ToString("0"); HeightInput.Text=e.Height.ToString("0"); FontInput.Text=e.FontSize.ToString("0");
            ColorInput.Text=e.Foreground; BackgroundInput.Text=e.Background; AnimationInput.SelectedItem=e.Animation; ShapeInput.SelectedItem=e.Shape;
            VisibleInput.IsChecked=e.Visible; TransparentBackgroundInput.IsChecked=e.TransparentBackground;
            MediaTagInput.Text=e.MediaTag;
            ImagePathText.Text=e.ImagePath??"No image selected";
        }
        _loading=false;
    }

    void Property_Changed(object sender,RoutedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,TextChangedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,SelectionChangedEventArgs e)=>ApplyProperties(sender);

    void ApplyProperties(object sender)
    {
        if(_loading||Selected is null)return;
        if(sender is ComboBox)
        {
            // Shape/animation changes need fresh geometry.
            SaveProperties(); Render(); return;
        }
        if(sender is CheckBox){SaveProperties();RefreshAllVisuals();return;}
        if(sender==ColorInput||sender==BackgroundInput){SaveProperties();RefreshAllVisuals();return;}
        _propertySave.Stop(); _propertySave.Start();
    }

    void SaveProperties()
    {
        if(_loading)return;
        var item=Selected; if(item is null)return;
        var name=NameInput.Text; var content=ContentInput.Text;
        var xOk=double.TryParse(XInput.Text,out var x); var yOk=double.TryParse(YInput.Text,out var y);
        var wOk=double.TryParse(WidthInput.Text,out var w); var hOk=double.TryParse(HeightInput.Text,out var h);
        var fOk=double.TryParse(FontInput.Text,out var f);
        var foregroundOk=TryColor(ColorInput.Text,out _); var backgroundOk=TryColor(BackgroundInput.Text,out _);
        var animation=AnimationInput.SelectedItem is ScorebugAnimation a?a:item.Animation;
        var shape=ShapeInput.SelectedItem is ScorebugShape s?s:item.Shape;
        var visible=VisibleInput.IsChecked==true; var transparent=TransparentBackgroundInput.IsChecked==true;
        var mediaTag=MediaTagInput.Text.Trim();
        if(item.Name==name&&item.Content==content&&(!xOk||item.X==x)&&(!yOk||item.Y==y)&&(!wOk||item.Width==w)&&(!hOk||item.Height==h)
            &&(!fOk||item.FontSize==f)&&(!foregroundOk||item.Foreground==ColorInput.Text)&&(!backgroundOk||item.Background==BackgroundInput.Text)
            &&item.Animation==animation&&item.Shape==shape&&item.Visible==visible&&item.TransparentBackground==transparent
            &&item.MediaTag==mediaTag)return;
        var id=item.Id;
        _catalog.Update(layout=>{
            var target=layout.Elements.FirstOrDefault(z=>z.Id==id); if(target is null)return;
            target.Name=name; target.Content=content;
            if(xOk)target.X=x; if(yOk)target.Y=y; if(wOk)target.Width=w; if(hOk)target.Height=h;
            if(fOk)target.FontSize=f;
            if(foregroundOk)target.Foreground=ColorInput.Text;
            if(backgroundOk)target.Background=BackgroundInput.Text;
            target.Animation=animation; target.Shape=shape;
            target.Visible=visible; target.TransparentBackground=transparent;
            target.MediaTag=mediaTag;
            if(target.Kind==ScorebugElementKind.Logo&&string.IsNullOrWhiteSpace(target.ImagePath))target.Content=$"LOGO · {target.Name}";
        });
    }

    static bool TryColor(string value,out Color color){try{color=(Color)ColorConverter.ConvertFromString(value)!;return true;}catch{color=Colors.White;return false;}}

    // New artwork starts with no background box so it lands clean on the canvas; turn one on in the properties if needed.
    void AddText_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New text",Content="EDIT TEXT",X=480,Y=470,TransparentBackground=true});
    void AddLogo_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New logo",Kind=ScorebugElementKind.Logo,Content="LOGO",X=520,Y=470,Width=140,Height=90,TransparentBackground=true});
    void AddShape_Click(object sender,RoutedEventArgs e)
    {
        var picker=new Window{Title="Add shape",Width=340,Height=190,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(14)};
        panel.Children.Add(new TextBlock{Text="Shape",Margin=new Thickness(4)});
        var shape=new ComboBox{ItemsSource=Enum.GetValues<ScorebugShape>(),SelectedItem=ScorebugShape.Rectangle,Margin=new Thickness(4)};
        var add=new Button{Content="Add shape",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right};
        add.Click+=(_,_)=>{
            var kind=(ScorebugShape)(shape.SelectedItem??ScorebugShape.Rectangle);
            Add(new ScorebugElement{Name=$"New {kind.ToString().ToLowerInvariant()}",Kind=ScorebugElementKind.Shape,Shape=kind,Content="",X=450,Y=470,Width=kind==ScorebugShape.Rectangle?200:140,Height=kind==ScorebugShape.Rectangle?100:140,TransparentBackground=true});
            picker.DialogResult=true; picker.Close();
        };
        panel.Children.Add(shape); panel.Children.Add(add); picker.Content=panel; ThemeManager.Apply(picker,_theme); picker.ShowDialog();
    }

    void AddData_Click(object sender,RoutedEventArgs e)
    {
        var choices=new List<(string,string)>{("Football · ball spot","{{BALL_SPOT}}"),("Football · down & distance","{{DOWNS}}"),("Football · yards to go","{{YARDS_TO_GO}}"),("Football · team yards","{{YARDS}}"),("Football · turnovers","{{TURNOVERS}}"),("Football · play clock","{{PLAY_CLOCK}}"),("Football · timeout clock","{{TIMEOUT_CLOCK}}"),("Football · down","{{DOWN}}"),("Football · to go","{{TO_GO}}"),("Football · ball on","{{BALL_ON}}"),("Timeouts left","{{TIMEOUTS}}"),("Possession","{{POSSESSION}}"),("Basketball · shot clock","{{SHOT_CLOCK}}"),("Basketball · fouls","{{BASKETBALL_FOULS}}"),("Baseball · count & outs","{{BASEBALL_COUNT}}"),("Baseball · hits","{{HITS}}"),("Soccer · cards","{{SOCCER_CARDS}}"),("Soccer · shots","{{SHOTS}}"),("Soccer · corners","{{CORNERS}}"),("Sport name","{{SPORT}}")};
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
        var tag=new ComboBox{IsEditable=true,Text=MediaLibrary.DefaultTag,ItemsSource=MediaLibrary.SuggestedTags.ToArray(),Margin=new Thickness(4)};
        panel.Children.Add(tag);
        panel.Children.Add(new TextBlock{Text="Only clips assigned this tag appear. Leave it empty to show whichever clip is playing.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(4,6,4,0)});
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
        _selectedId=item.Id;
        Render();
    }

    void Remove_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not { } item)return;
        var id=item.Id;
        _catalog.Update(layout=>layout.Elements.RemoveAll(x=>x.Id==id));
        _selectedId=null;
        Render();
    }

    void ChooseImage_Click(object sender,RoutedEventArgs e)
    {
        if(Selected is not { } selected)return;
        var dialog=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*"};
        if(dialog.ShowDialog(this)!=true)return;
        var id=selected.Id;
        try
        {
            var assetFolder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Scoreboard","ScorebugAssets");
            Directory.CreateDirectory(assetFolder);
            var target=System.IO.Path.Combine(assetFolder,$"{Guid.NewGuid():N}{System.IO.Path.GetExtension(dialog.FileName)}");
            File.Copy(dialog.FileName,target);
            _catalog.Update(layout=>{
                var item=layout.Elements.FirstOrDefault(x=>x.Id==id); if(item is null)return;
                item.Kind=ScorebugElementKind.Logo; item.ImagePath=target; item.Content=System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            });
        }
        catch(Exception ex){MessageBox.Show(this,$"Could not import logo: {ex.Message}","Scorebug Studio",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }

    void Element_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(_resizing||sender is not Border host||host.Tag is not ScorebugElement item)return;
        _dragElement=item;
        _selectedId=item.Id;
        var p=e.GetPosition(DesignCanvas);
        _dragOffset=new Point(p.X-item.X,p.Y-item.Y);
        _syncingLayers=true;
        Layers.SelectedItem=Layers.Items.Cast<LayerItem>().FirstOrDefault(x=>x.Element.Id==item.Id);
        _syncingLayers=false;
        RefreshAllVisuals();
        LoadProperties();
        host.CaptureMouse();
        e.Handled=true;
    }

    void Element_MouseMove(object sender,MouseEventArgs e)
    {
        if(_resizing||_dragElement is null||e.LeftButton!=MouseButtonState.Pressed)return;
        var p=e.GetPosition(DesignCanvas);
        var x=Math.Clamp(p.X-_dragOffset.X,0,Math.Max(0,_catalog.Layout.Width-_dragElement.Width));
        var y=Math.Clamp(p.Y-_dragOffset.Y,0,Math.Max(0,_catalog.Layout.Height-_dragElement.Height));
        _dragElement.X=x; _dragElement.Y=y;
        if(sender is Border host){Canvas.SetLeft(host,x);Canvas.SetTop(host,y);}
        if(!_loading){_loading=true;XInput.Text=x.ToString("0");YInput.Text=y.ToString("0");_loading=false;}
    }

    void Element_MouseUp(object sender,MouseButtonEventArgs e)
    {
        if(_resizing)return;
        if(sender is Border host)host.ReleaseMouseCapture();
        if(_dragElement is { } item)
        {
            var id=item.Id; var x=item.X; var y=item.Y;
            _dragElement=null;
            _catalog.Update(layout=>{var target=layout.Elements.FirstOrDefault(z=>z.Id==id);if(target is not null){target.X=x;target.Y=y;}});
        }
        e.Handled=true;
    }

    void Canvas_MouseMove(object sender,MouseEventArgs e) { }

    void Canvas_MouseUp(object sender,MouseButtonEventArgs e)
    {
        if(_resizing)return;
        if(_dragElement is { } item)
        {
            var id=item.Id; var x=item.X; var y=item.Y;
            _dragElement=null;
            _catalog.Update(layout=>{var target=layout.Elements.FirstOrDefault(z=>z.Id==id);if(target is not null){target.X=x;target.Y=y;}});
        }
    }

    void Resize_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(sender is not Border handle||handle.Tag is not ScorebugElement item)return;
        _dragElement=item; _resizing=true; _selectedId=item.Id;
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

    static Color Hsv(double hue,double saturation,double value)
    {
        var chroma=value*saturation; var x=chroma*(1-Math.Abs((hue/60%2)-1)); var m=value-chroma;
        var (r,g,b)=hue switch{<60=>(chroma,x,0d),<120=>(x,chroma,0d),<180=>(0d,chroma,x),<240=>(0d,x,chroma),<300=>(x,0d,chroma),_=>(chroma,0d,x)};
        return Color.FromRgb((byte)((r+m)*255),(byte)((g+m)*255),(byte)((b+m)*255));
    }

    void TextColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(ColorInput);
    void BackgroundColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(BackgroundInput);
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
        base.OnPreviewKeyDown(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        _propertySave.Stop(); SaveProperties();
        foreach(var surface in _videoSurfaces) surface.Dispose();
        _videoSurfaces.Clear();
        _catalog.Changed-=Render; _engine.Changed-=RefreshBoundText;
        base.OnClosed(e);
    }
    sealed record LayerItem(ScorebugElement Element){public override string ToString()=>$"{(Element.Visible?"◉":"○")}  {Element.Name}";}
}
