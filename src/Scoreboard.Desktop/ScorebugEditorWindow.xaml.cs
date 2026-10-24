using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;
public partial class ScorebugEditorWindow : Window
{
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    bool _loading;
    readonly System.Windows.Threading.DispatcherTimer _propertySave = new() { Interval=TimeSpan.FromMilliseconds(350) };
    ScorebugElement? _selected;
    ScorebugElement? _dragElement;
    bool _resizing;
    double _resizeStartX, _resizeStartY, _resizeStartWidth, _resizeStartHeight;
    Point _dragOffset;
    Point _listStart;
    public ScorebugEditorWindow(ScorebugCatalog catalog, GameEngine engine)
    {
        InitializeComponent(); _catalog=catalog; _engine=engine;
        AnimationInput.ItemsSource=Enum.GetValues<ScorebugAnimation>();
        _propertySave.Tick+=(_,_)=>{_propertySave.Stop();SaveProperties();Render();};
        _catalog.Changed += Render; _engine.Changed += Render; Render();
    }
    void Render()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Render);return;}
        _loading=true; Layers.ItemsSource=null; Layers.ItemsSource=_catalog.Layout.Elements.Select(e=>new LayerItem(e)).ToArray();
        if(_selected is not null) Layers.SelectedItem=Layers.Items.Cast<LayerItem>().FirstOrDefault(x=>x.Element.Id==_selected.Id);
        DesignCanvas.Children.Clear();
        foreach(var item in _catalog.Layout.Elements) DesignCanvas.Children.Add(CreateVisual(item));
        _loading=false; LoadProperties();
    }
    UIElement CreateVisual(ScorebugElement item)
    {
        FrameworkElement content;
        if(item.Kind==ScorebugElementKind.Logo && !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
        {
            try { content=new Image{Source=new BitmapImage(new Uri(item.ImagePath!)),Stretch=Stretch.Uniform}; }
            catch { content=Placeholder(item); }
        }
        else if(item.Kind==ScorebugElementKind.Shape)
            content=new Rectangle{Fill=Brush(item.Background),Stroke=Brush(item.Foreground),StrokeThickness=2,RadiusX=10,RadiusY=10};
        else content=Placeholder(item);
        var host=new Border { Width=item.Width, Height=item.Height, Background=item.Kind==ScorebugElementKind.Text&&!item.TransparentBackground?Brush(item.Background):Brushes.Transparent, Opacity=item.Opacity, Cursor=Cursors.SizeAll, BorderBrush=item.Id==_selected?.Id?Brushes.DeepSkyBlue:Brushes.Transparent, BorderThickness=new Thickness(item.Id==_selected?.Id?2:1), Tag=item };
        Canvas.SetLeft(host,item.X); Canvas.SetTop(host,item.Y); Panel.SetZIndex(host,_catalog.Layout.Elements.IndexOf(item)); host.MouseLeftButtonDown+=Element_MouseDown; host.MouseMove+=Element_MouseMove; host.MouseLeftButtonUp+=Element_MouseUp;
        if(item.Animation==ScorebugAnimation.Pulse){var animation=new DoubleAnimation(.72,1,TimeSpan.FromSeconds(1.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever};host.BeginAnimation(OpacityProperty,animation);}
        if(item.Animation==ScorebugAnimation.Drift){host.RenderTransform=new TranslateTransform();host.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,10,TimeSpan.FromSeconds(2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});}
        if(item.Id==_selected?.Id){var adorn=new Canvas{IsHitTestVisible=true};var contentGrid=new Grid{Width=item.Width,Height=item.Height};contentGrid.Children.Add(content);adorn.Children.Add(contentGrid);var handle=new Border{Width=18,Height=18,Background=Brushes.DeepSkyBlue,BorderBrush=Brushes.White,BorderThickness=new Thickness(1),Cursor=Cursors.SizeNWSE,Tag=item};Canvas.SetLeft(handle,item.Width-18);Canvas.SetTop(handle,item.Height-18);handle.PreviewMouseLeftButtonDown+=Resize_MouseDown;handle.PreviewMouseMove+=Resize_MouseMove;handle.PreviewMouseLeftButtonUp+=Resize_MouseUp;adorn.Children.Add(handle);host.Child=adorn;}else host.Child=content;
        return host;
    }
    TextBlock Placeholder(ScorebugElement item)
    {
        var state=_engine.State;
        var content=ScoreboardTemplates.Bind(item.Content,state);
        if(item.Kind==ScorebugElementKind.Logo && string.IsNullOrWhiteSpace(item.ImagePath))content="＋  "+(string.IsNullOrWhiteSpace(content)?"LOGO":content);
        return new TextBlock{Text=content,Foreground=Brush(item.Foreground),FontSize=item.FontSize,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    }
    static Brush Brush(string value)=> (Brush)new BrushConverter().ConvertFromString(value)!;
    void Layers_SelectionChanged(object sender,SelectionChangedEventArgs e){if(_loading)return;_selected=(Layers.SelectedItem as LayerItem)?.Element;Render();}
    void LoadProperties()
    {
        _loading=true; var e=_selected; var has=e is not null;
        NameInput.IsEnabled=ContentInput.IsEnabled=XInput.IsEnabled=YInput.IsEnabled=WidthInput.IsEnabled=HeightInput.IsEnabled=FontInput.IsEnabled=ColorInput.IsEnabled=BackgroundInput.IsEnabled=AnimationInput.IsEnabled=VisibleInput.IsEnabled=TransparentBackgroundInput.IsEnabled=has;
        if(has){NameInput.Text=e!.Name;ContentInput.Text=e.Content;XInput.Text=e.X.ToString("0");YInput.Text=e.Y.ToString("0");WidthInput.Text=e.Width.ToString("0");HeightInput.Text=e.Height.ToString("0");FontInput.Text=e.FontSize.ToString("0");ColorInput.Text=e.Foreground;BackgroundInput.Text=e.Background;AnimationInput.SelectedItem=e.Animation;VisibleInput.IsChecked=e.Visible;TransparentBackgroundInput.IsChecked=e.TransparentBackground;ImagePathText.Text=e.ImagePath??"No image selected";}
        _loading=false;
    }
    void Property_Changed(object sender,RoutedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,TextChangedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,SelectionChangedEventArgs e)=>ApplyProperties(sender);
    void ApplyProperties(object sender)
    {
        if(_loading||_selected is null)return;
        if(sender is CheckBox){SaveProperties();Render();return;}
        if(sender is ComboBox){SaveProperties();return;}
        if(sender==ColorInput||sender==BackgroundInput){SaveProperties();Render();return;}
        _propertySave.Stop();_propertySave.Start();
    }
    void SaveProperties()
    {
        if(_loading||_selected is null)return;
        var item=_selected;
        var name=NameInput.Text;var content=ContentInput.Text;
        var xOk=double.TryParse(XInput.Text,out var x);var yOk=double.TryParse(YInput.Text,out var y);
        var wOk=double.TryParse(WidthInput.Text,out var w);var hOk=double.TryParse(HeightInput.Text,out var h);var fOk=double.TryParse(FontInput.Text,out var f);
        var foregroundOk=TryColor(ColorInput.Text,out _);var backgroundOk=TryColor(BackgroundInput.Text,out _);
        if(item.Name==name&&item.Content==content&&(!xOk||item.X==x)&&(!yOk||item.Y==y)&&(!wOk||item.Width==w)&&(!hOk||item.Height==h)&&(!fOk||item.FontSize==f)&&(!foregroundOk||item.Foreground==ColorInput.Text)&&(!backgroundOk||item.Background==BackgroundInput.Text)&&item.Animation==(AnimationInput.SelectedItem is ScorebugAnimation animation?animation:item.Animation)&&item.Visible==(VisibleInput.IsChecked==true)&&item.TransparentBackground==(TransparentBackgroundInput.IsChecked==true))return;
        _catalog.Update(layout=>{var target=layout.Elements.FirstOrDefault(x=>x.Id==item.Id);if(target is null)return;
            target.Name=name;target.Content=content;
            if(xOk)target.X=x;if(yOk)target.Y=y;if(wOk)target.Width=w;if(hOk)target.Height=h;
            if(fOk)target.FontSize=f;
            if(foregroundOk)target.Foreground=ColorInput.Text;
            if(backgroundOk)target.Background=BackgroundInput.Text;
            if(AnimationInput.SelectedItem is ScorebugAnimation selectedAnimation)target.Animation=selectedAnimation;
            target.Visible=VisibleInput.IsChecked==true;
            target.TransparentBackground=TransparentBackgroundInput.IsChecked==true;
            if(target.Kind==ScorebugElementKind.Logo && string.IsNullOrWhiteSpace(target.ImagePath))target.Content=$"LOGO · {target.Name}";
        });
    }
    static bool TryColor(string value,out Color color){try{color=(Color)ColorConverter.ConvertFromString(value)!;return true;}catch{color=Colors.White;return false;}}
    void AddText_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New text",Content="EDIT TEXT",X=480,Y=470});
    void AddLogo_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New logo",Kind=ScorebugElementKind.Logo,Content="LOGO",X=520,Y=470,Width=140,Height=90});
    void AddShape_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New shape",Kind=ScorebugElementKind.Shape,Content="",X=450,Y=470,Width=250,Height=90});
    void AddData_Click(object sender,RoutedEventArgs e){var choices=new[]{("Football · ball spot","{{BALL_SPOT}}"),("Football · down & distance","{{DOWNS}}"),("Football · yards to go","{{YARDS_TO_GO}}"),("Football · team yards","{{YARDS}}"),("Football · turnovers","{{TURNOVERS}}"),("Basketball · shot clock","{{SHOT_CLOCK}}"),("Basketball · fouls","{{BASKETBALL_FOULS}}"),("Baseball · count & outs","{{BASEBALL_COUNT}}"),("Baseball · hits","{{HITS}}"),("Soccer · cards","{{SOCCER_CARDS}}"),("Soccer · shots","{{SHOTS}}"),("Soccer · corners","{{CORNERS}}"),("Sport name","{{SPORT}}")};var picker=new Window{Title="Add sport data",Width=380,Height=470,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};var list=new ListBox{ItemsSource=choices.Select(x=>x.Item1).ToArray(),Margin=new Thickness(10)};var add=new Button{Content="Add selected data",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right};add.Click+=(_,_)=>{if(list.SelectedIndex<0)return;var token=choices[list.SelectedIndex].Item2;Add(new ScorebugElement{Name=choices[list.SelectedIndex].Item1,Content=token,X=420,Y=470});picker.DialogResult=true;picker.Close();};picker.Content=new DockPanel{Children={add,list}};DockPanel.SetDock(add,Dock.Bottom);ThemeManager.Apply(picker,Foreground is SolidColorBrush brush&&brush.Color==Color.FromRgb(24,32,43)?"Light":"Dark");picker.ShowDialog();}
    void Add(ScorebugElement item){_catalog.Update(layout=>layout.Elements.Add(item));_selected=item;Render();}
    void Remove_Click(object sender,RoutedEventArgs e){if(_selected is null)return;var id=_selected.Id;_catalog.Update(layout=>layout.Elements.RemoveAll(x=>x.Id==id));_selected=null;Render();}
    void ChooseImage_Click(object sender,RoutedEventArgs e){if(_selected is null)return;var dialog=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*"};if(dialog.ShowDialog(this)!=true)return;var id=_selected.Id;try{var assetFolder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Scoreboard","ScorebugAssets");Directory.CreateDirectory(assetFolder);var target=System.IO.Path.Combine(assetFolder,$"{Guid.NewGuid():N}{System.IO.Path.GetExtension(dialog.FileName)}");File.Copy(dialog.FileName,target);_catalog.Update(layout=>{var item=layout.Elements.First(x=>x.Id==id);item.Kind=ScorebugElementKind.Logo;item.ImagePath=target;item.Content=System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);});}catch(Exception ex){MessageBox.Show(this,$"Could not import logo: {ex.Message}","Scorebug Studio",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    void Element_MouseDown(object sender,MouseButtonEventArgs e){if(_resizing||sender is not Border b||b.Tag is not ScorebugElement item)return;_propertySave.Stop();SaveProperties();_resizing=false;_selected=item;_dragElement=item;var p=e.GetPosition(DesignCanvas);_dragOffset=new Point(p.X-item.X,p.Y-item.Y);b.BorderBrush=Brushes.DeepSkyBlue;b.BorderThickness=new Thickness(2);_loading=true;Layers.SelectedItem=Layers.Items.Cast<LayerItem>().FirstOrDefault(x=>x.Element.Id==item.Id);_loading=false;b.CaptureMouse();e.Handled=true;}
    void Element_MouseMove(object sender,MouseEventArgs e){if(_resizing||_dragElement is null||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(DesignCanvas);var x=Math.Clamp(p.X-_dragOffset.X,0,Math.Max(0,1280-_dragElement.Width));var y=Math.Clamp(p.Y-_dragOffset.Y,0,Math.Max(0,720-_dragElement.Height));_dragElement.X=x;_dragElement.Y=y;if(sender is Border b){Canvas.SetLeft(b,x);Canvas.SetTop(b,y);}}
    void Element_MouseUp(object sender,MouseButtonEventArgs e){if(_resizing)return;if(sender is Border b)b.ReleaseMouseCapture();if(_dragElement is not null){var id=_dragElement.Id;var x=_dragElement.X;var y=_dragElement.Y;_catalog.Update(layout=>{var item=layout.Elements.First(z=>z.Id==id);item.X=x;item.Y=y;});}_dragElement=null;e.Handled=true;}
    void Canvas_MouseMove(object sender,MouseEventArgs e){if(_dragElement is not null)return;}
    void Canvas_MouseUp(object sender,MouseButtonEventArgs e){if(_resizing)return;if(_dragElement is not null){var id=_dragElement.Id;var x=_dragElement.X;var y=_dragElement.Y;_catalog.Update(layout=>{var item=layout.Elements.First(z=>z.Id==id);item.X=x;item.Y=y;});}_dragElement=null;}
    void Layers_MouseDown(object sender,MouseButtonEventArgs e)=>_listStart=e.GetPosition(null);
    void Layers_MouseMove(object sender,MouseEventArgs e){if(e.LeftButton!=MouseButtonState.Pressed||Layers.SelectedItem is null)return;var p=e.GetPosition(null);if(Math.Abs(p.X-_listStart.X)>8||Math.Abs(p.Y-_listStart.Y)>8)DragDrop.DoDragDrop(Layers,((LayerItem)Layers.SelectedItem).Element.Id,DragDropEffects.Move);}
    void Layers_Drop(object sender,DragEventArgs e){if(e.Data.GetData(typeof(string)) is not string movedId)return;var p=e.GetPosition(Layers);var target=Layers.InputHitTest(p) as DependencyObject;while(target is not null&&target is not ListBoxItem)target=VisualTreeHelper.GetParent(target);if(target is not ListBoxItem row||row.Content is not LayerItem drop)return;var from=_catalog.Layout.Elements.FindIndex(x=>x.Id==movedId);var to=_catalog.Layout.Elements.FindIndex(x=>x.Id==drop.Element.Id);if(from<0||to<0||from==to)return;_catalog.Update(layout=>{var item=layout.Elements[from];layout.Elements.RemoveAt(from);layout.Elements.Insert(Math.Clamp(to,0,layout.Elements.Count),item);});}
    void Resize_MouseDown(object sender,MouseButtonEventArgs e){if(sender is not Border b||b.Tag is not ScorebugElement item)return;_propertySave.Stop();SaveProperties();_selected=item;_dragElement=item;_resizing=true;var origin=e.GetPosition(DesignCanvas);_resizeStartX=origin.X;_resizeStartY=origin.Y;_resizeStartWidth=item.Width;_resizeStartHeight=item.Height;Mouse.Capture(b,CaptureMode.Element);e.Handled=true;}
    void Resize_MouseMove(object sender,MouseEventArgs e){if(_dragElement is null||!_resizing||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(DesignCanvas);_dragElement.Width=Math.Clamp(_resizeStartWidth+p.X-_resizeStartX,24,Math.Max(24,1280-_dragElement.X));_dragElement.Height=Math.Clamp(_resizeStartHeight+p.Y-_resizeStartY,24,Math.Max(24,720-_dragElement.Y));if(sender is Border handle&&handle.Parent is Panel adorn&&adorn.Parent is Border host){host.Width=_dragElement.Width;host.Height=_dragElement.Height;Canvas.SetLeft(handle,_dragElement.Width-handle.Width);Canvas.SetTop(handle,_dragElement.Height-handle.Height);if(adorn.Children[0] is FrameworkElement content){content.Width=_dragElement.Width;content.Height=_dragElement.Height;}}e.Handled=true;}
    void Resize_MouseUp(object sender,MouseButtonEventArgs e){if(sender is Border b)b.ReleaseMouseCapture();if(_dragElement is not null){var id=_dragElement.Id;var w=_dragElement.Width;var h=_dragElement.Height;_catalog.Update(layout=>{var item=layout.Elements.First(z=>z.Id==id);item.Width=w;item.Height=h;});}_dragElement=null;_resizing=false;e.Handled=true;}
    void ChoosePresetColor(TextBox target){var chooser=new Window{Title="Choose color",Width=480,Height=330,MinWidth=420,MinHeight=300,ResizeMode=ResizeMode.CanResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(23,28,37)),Foreground=Brushes.White};var root=new DockPanel{Margin=new Thickness(14)};var controls=new StackPanel();DockPanel.SetDock(controls,Dock.Top);var colorPreview=new Border{Height=52,CornerRadius=new CornerRadius(6),Margin=new Thickness(4),BorderThickness=new Thickness(1),BorderBrush=Brushes.Gray};var hex=new TextBox{Text=target.Text,MinWidth=110};var choose=new Button{Content="Use selected color",IsDefault=true};var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(new TextBlock{Text="Hex:",VerticalAlignment=VerticalAlignment.Center});row.Children.Add(hex);row.Children.Add(choose);controls.Children.Add(colorPreview);controls.Children.Add(row);var rainbow=new System.Windows.Controls.Primitives.UniformGrid{Columns=12,Rows=3,Margin=new Thickness(0,12,0,8),Background=Brushes.Transparent};for(var i=0;i<36;i++){var hue=i*10d;var swatch=new Button{Background=new SolidColorBrush(Hsv(hue,1,1)),BorderBrush=Brushes.White,BorderThickness=new Thickness(1),Height=30,Margin=new Thickness(2),ToolTip=$"Hue {hue:0}°"};swatch.Click+=(_,_)=>{var c=((SolidColorBrush)swatch.Background).Color;hex.Text=$"#{c.R:X2}{c.G:X2}{c.B:X2}";};rainbow.Children.Add(swatch);}var shades=new WrapPanel();foreach(var c in new[]{Colors.White,Colors.LightGray,Colors.Gray,Colors.DimGray,Colors.Black,Colors.Red,Colors.Orange,Colors.Yellow,Colors.LimeGreen,Colors.Cyan,Colors.Blue,Colors.Magenta}){var swatch=new Button{Background=new SolidColorBrush(c),BorderBrush=Brushes.White,BorderThickness=new Thickness(1),Width=34,Height=28,Margin=new Thickness(2),ToolTip=c.ToString()};swatch.Click+=(_,_)=>hex.Text=$"#{c.R:X2}{c.G:X2}{c.B:X2}";shades.Children.Add(swatch);}var area=new StackPanel();area.Children.Add(rainbow);area.Children.Add(shades);root.Children.Add(controls);root.Children.Add(area);void UpdatePreview(){try{var color=(Color)ColorConverter.ConvertFromString(hex.Text)!;colorPreview.Background=new SolidColorBrush(color);choose.IsEnabled=true;}catch{colorPreview.Background=Brushes.Transparent;choose.IsEnabled=false;}}hex.TextChanged+=(_,_)=>UpdatePreview();UpdatePreview();choose.Click+=(_,_)=>{target.Text=hex.Text;chooser.DialogResult=true;chooser.Close();};chooser.Content=root;chooser.ShowDialog();}
    static Color Hsv(double hue,double saturation,double value){var chroma=value*saturation;var x=chroma*(1-Math.Abs((hue/60%2)-1));var m=value-chroma;var (r,g,b)=hue switch{<60=>(chroma,x,0d),<120=>(x,chroma,0d),<180=>(0d,chroma,x),<240=>(0d,x,chroma),<300=>(x,0d,chroma),_=>(chroma,0d,x)};return Color.FromRgb((byte)((r+m)*255),(byte)((g+m)*255),(byte)((b+m)*255));}
    void TextColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(ColorInput);
    void BackgroundColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(BackgroundInput);
    void Fit_Click(object sender,RoutedEventArgs e){if(CanvasFrame.ActualWidth<10||CanvasFrame.ActualHeight<10)return;var scale=Math.Min((CanvasFrame.ActualWidth-8)/1280,(CanvasFrame.ActualHeight-8)/720);CanvasScale.ScaleX=CanvasScale.ScaleY=Math.Clamp(scale,.2,1);}
    void Done_Click(object sender,RoutedEventArgs e)=>Close();
    protected override void OnClosed(EventArgs e){_propertySave.Stop();SaveProperties();_catalog.Changed-=Render;_engine.Changed-=Render;base.OnClosed(e);}
    sealed record LayerItem(ScorebugElement Element){public override string ToString()=>$"{(Element.Visible?"◉":"○")}  {Element.Name}";}
}
