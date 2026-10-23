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
    Point _dragOffset;
    Point _listStart;
    public ScorebugEditorWindow(ScorebugCatalog catalog, GameEngine engine)
    {
        InitializeComponent(); _catalog=catalog; _engine=engine;
        AnimationInput.ItemsSource=Enum.GetValues<ScorebugAnimation>();
        _propertySave.Tick+=(_,_)=>{_propertySave.Stop();SaveProperties();};
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
        var host=new Border { Width=item.Width, Height=item.Height, Background=item.Kind==ScorebugElementKind.Text?Brush(item.Background):Brushes.Transparent, Opacity=item.Opacity, Cursor=Cursors.SizeAll, BorderBrush=item.Id==_selected?.Id?Brushes.DeepSkyBlue:Brushes.Transparent, BorderThickness=new Thickness(item.Id==_selected?.Id?2:1), Tag=item };
        Canvas.SetLeft(host,item.X); Canvas.SetTop(host,item.Y); Panel.SetZIndex(host,_catalog.Layout.Elements.IndexOf(item)); host.MouseLeftButtonDown+=Element_MouseDown; host.MouseMove+=Element_MouseMove; host.MouseLeftButtonUp+=Element_MouseUp;
        if(item.Animation==ScorebugAnimation.Pulse){var animation=new DoubleAnimation(.72,1,TimeSpan.FromSeconds(1.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever};host.BeginAnimation(OpacityProperty,animation);}
        if(item.Animation==ScorebugAnimation.Drift){host.RenderTransform=new TranslateTransform();host.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,10,TimeSpan.FromSeconds(2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});}
        if(item.Id==_selected?.Id){var adorn=new Canvas{IsHitTestVisible=true};var contentGrid=new Grid{Width=item.Width,Height=item.Height};contentGrid.Children.Add(content);adorn.Children.Add(contentGrid);var handle=new Border{Width=18,Height=18,Background=Brushes.DeepSkyBlue,BorderBrush=Brushes.White,BorderThickness=new Thickness(1),Cursor=Cursors.SizeNWSE,Tag=item};Canvas.SetLeft(handle,item.Width-18);Canvas.SetTop(handle,item.Height-18);handle.MouseLeftButtonDown+=Resize_MouseDown;handle.MouseMove+=Resize_MouseMove;handle.MouseLeftButtonUp+=Resize_MouseUp;adorn.Children.Add(handle);host.Child=adorn;}else host.Child=content;
        return host;
    }
    TextBlock Placeholder(ScorebugElement item)
    {
        var content=item.Content;
        var state=_engine.State;
        content=content.Replace("{{HOME_TEAM}}",state.HomeAbbreviation).Replace("{{AWAY_TEAM}}",state.AwayAbbreviation).Replace("{{HOME_SCORE}}",state.HomeScore.ToString()).Replace("{{AWAY_SCORE}}",state.AwayScore.ToString()).Replace("{{PERIOD}}",$"{state.PeriodLabel.ToUpperInvariant()} {state.Period}").Replace("{{GAME_CLOCK}}",GameEngine.FormatClock(state.ClockSeconds));
        if(item.Kind==ScorebugElementKind.Logo && string.IsNullOrWhiteSpace(item.ImagePath))content="＋  "+(string.IsNullOrWhiteSpace(content)?"LOGO":content);
        return new TextBlock{Text=content,Foreground=Brush(item.Foreground),FontSize=item.FontSize,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    }
    static Brush Brush(string value)=> (Brush)new BrushConverter().ConvertFromString(value)!;
    void Layers_SelectionChanged(object sender,SelectionChangedEventArgs e){if(_loading)return;_selected=(Layers.SelectedItem as LayerItem)?.Element;Render();}
    void LoadProperties()
    {
        _loading=true; var e=_selected; var has=e is not null;
        NameInput.IsEnabled=ContentInput.IsEnabled=XInput.IsEnabled=YInput.IsEnabled=WidthInput.IsEnabled=HeightInput.IsEnabled=FontInput.IsEnabled=ColorInput.IsEnabled=BackgroundInput.IsEnabled=AnimationInput.IsEnabled=VisibleInput.IsEnabled=has;
        if(has){NameInput.Text=e!.Name;ContentInput.Text=e.Content;XInput.Text=e.X.ToString("0");YInput.Text=e.Y.ToString("0");WidthInput.Text=e.Width.ToString("0");HeightInput.Text=e.Height.ToString("0");FontInput.Text=e.FontSize.ToString("0");ColorInput.Text=e.Foreground;BackgroundInput.Text=e.Background;AnimationInput.SelectedItem=e.Animation;VisibleInput.IsChecked=e.Visible;ImagePathText.Text=e.ImagePath??"No image selected";}
        _loading=false;
    }
    void Property_Changed(object sender,RoutedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,TextChangedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,SelectionChangedEventArgs e)=>ApplyProperties(sender);
    void ApplyProperties(object sender)
    {
        if(_loading||_selected is null)return;
        if(sender is CheckBox){SaveProperties();return;}
        if(sender is ComboBox){SaveProperties();return;}
        _propertySave.Stop();_propertySave.Start();
    }
    void SaveProperties()
    {
        if(_loading||_selected is null)return;
        var id=_selected.Id;
        _catalog.Update(layout=>{var item=layout.Elements.FirstOrDefault(x=>x.Id==id);if(item is null)return;
            item.Name=NameInput.Text;item.Content=ContentInput.Text;
            if(double.TryParse(XInput.Text,out var x))item.X=x;if(double.TryParse(YInput.Text,out var y))item.Y=y;
            if(double.TryParse(WidthInput.Text,out var w))item.Width=w;if(double.TryParse(HeightInput.Text,out var h))item.Height=h;
            if(double.TryParse(FontInput.Text,out var f))item.FontSize=f;
            if(TryColor(ColorInput.Text,out _))item.Foreground=ColorInput.Text;
            if(TryColor(BackgroundInput.Text,out _))item.Background=BackgroundInput.Text;
            if(AnimationInput.SelectedItem is ScorebugAnimation animation)item.Animation=animation;
            item.Visible=VisibleInput.IsChecked==true;
            if(item.Kind==ScorebugElementKind.Logo && string.IsNullOrWhiteSpace(item.ImagePath))item.Content=$"LOGO · {item.Name}";
        });
    }
    static bool TryColor(string value,out Color color){try{color=(Color)ColorConverter.ConvertFromString(value)!;return true;}catch{color=Colors.White;return false;}}
    void AddText_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New text",Content="EDIT TEXT",X=480,Y=470});
    void AddLogo_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New logo",Kind=ScorebugElementKind.Logo,Content="LOGO",X=520,Y=470,Width=140,Height=90});
    void AddShape_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New shape",Kind=ScorebugElementKind.Shape,Content="",X=450,Y=470,Width=250,Height=90});
    void Add(ScorebugElement item){_catalog.Update(layout=>layout.Elements.Add(item));_selected=item;Render();}
    void Remove_Click(object sender,RoutedEventArgs e){if(_selected is null)return;var id=_selected.Id;_catalog.Update(layout=>layout.Elements.RemoveAll(x=>x.Id==id));_selected=null;Render();}
    void ChooseImage_Click(object sender,RoutedEventArgs e){if(_selected is null)return;var dialog=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*"};if(dialog.ShowDialog(this)!=true)return;var id=_selected.Id;try{var assetFolder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Scoreboard","ScorebugAssets");Directory.CreateDirectory(assetFolder);var target=System.IO.Path.Combine(assetFolder,$"{Guid.NewGuid():N}{System.IO.Path.GetExtension(dialog.FileName)}");File.Copy(dialog.FileName,target);_catalog.Update(layout=>{var item=layout.Elements.First(x=>x.Id==id);item.Kind=ScorebugElementKind.Logo;item.ImagePath=target;item.Content=System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);});}catch(Exception ex){MessageBox.Show(this,$"Could not import logo: {ex.Message}","Scorebug Studio",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    void Element_MouseDown(object sender,MouseButtonEventArgs e){if(sender is not Border b||b.Tag is not ScorebugElement item)return;_propertySave.Stop();SaveProperties();_resizing=false;_selected=item;_dragElement=item;var p=e.GetPosition(DesignCanvas);_dragOffset=new Point(p.X-item.X,p.Y-item.Y);b.BorderBrush=Brushes.DeepSkyBlue;b.BorderThickness=new Thickness(2);_loading=true;Layers.SelectedItem=Layers.Items.Cast<LayerItem>().FirstOrDefault(x=>x.Element.Id==item.Id);_loading=false;b.CaptureMouse();e.Handled=true;}
    void Element_MouseMove(object sender,MouseEventArgs e){if(_dragElement is null||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(DesignCanvas);var x=Math.Clamp(p.X-_dragOffset.X,0,1280-_dragElement.Width);var y=Math.Clamp(p.Y-_dragOffset.Y,0,720-_dragElement.Height);_dragElement.X=x;_dragElement.Y=y;if(sender is Border b){Canvas.SetLeft(b,x);Canvas.SetTop(b,y);}}
    void Element_MouseUp(object sender,MouseButtonEventArgs e){if(sender is Border b)b.ReleaseMouseCapture();if(_dragElement is not null){var id=_dragElement.Id;var x=_dragElement.X;var y=_dragElement.Y;_catalog.Update(layout=>{var item=layout.Elements.First(z=>z.Id==id);item.X=x;item.Y=y;});}_dragElement=null;}
    void Canvas_MouseMove(object sender,MouseEventArgs e){if(_dragElement is not null)return;}
    void Canvas_MouseUp(object sender,MouseButtonEventArgs e)=>_dragElement=null;
    void Layers_MouseDown(object sender,MouseButtonEventArgs e)=>_listStart=e.GetPosition(null);
    void Layers_MouseMove(object sender,MouseEventArgs e){if(e.LeftButton!=MouseButtonState.Pressed||Layers.SelectedItem is null)return;var p=e.GetPosition(null);if(Math.Abs(p.X-_listStart.X)>8||Math.Abs(p.Y-_listStart.Y)>8)DragDrop.DoDragDrop(Layers,((LayerItem)Layers.SelectedItem).Element.Id,DragDropEffects.Move);}
    void Layers_Drop(object sender,DragEventArgs e){if(e.Data.GetData(typeof(string)) is not string movedId)return;var p=e.GetPosition(Layers);var target=Layers.InputHitTest(p) as DependencyObject;while(target is not null&&target is not ListBoxItem)target=VisualTreeHelper.GetParent(target);if(target is not ListBoxItem row||row.Content is not LayerItem drop)return;var from=_catalog.Layout.Elements.FindIndex(x=>x.Id==movedId);var to=_catalog.Layout.Elements.FindIndex(x=>x.Id==drop.Element.Id);if(from<0||to<0||from==to)return;_catalog.Update(layout=>{var item=layout.Elements[from];layout.Elements.RemoveAt(from);layout.Elements.Insert(Math.Clamp(to,0,layout.Elements.Count),item);});}
    void Resize_MouseDown(object sender,MouseButtonEventArgs e){if(sender is not Border b||b.Tag is not ScorebugElement item)return;_propertySave.Stop();SaveProperties();_selected=item;_dragElement=item;_resizing=true;b.CaptureMouse();e.Handled=true;}
    void Resize_MouseMove(object sender,MouseEventArgs e){if(_dragElement is null||!_resizing||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(DesignCanvas);_dragElement.Width=Math.Clamp(p.X-_dragElement.X,24,1280-_dragElement.X);_dragElement.Height=Math.Clamp(p.Y-_dragElement.Y,24,720-_dragElement.Y);if(sender is Border handle&&handle.Parent is Panel adorn&&adorn.Parent is Border host){host.Width=_dragElement.Width;host.Height=_dragElement.Height;Canvas.SetLeft(handle,_dragElement.Width-handle.Width);Canvas.SetTop(handle,_dragElement.Height-handle.Height);if(adorn.Children[0] is FrameworkElement content) {content.Width=_dragElement.Width;content.Height=_dragElement.Height;}}}
    void Resize_MouseUp(object sender,MouseButtonEventArgs e){if(sender is Border b)b.ReleaseMouseCapture();if(_dragElement is not null){var id=_dragElement.Id;var w=_dragElement.Width;var h=_dragElement.Height;_catalog.Update(layout=>{var item=layout.Elements.First(z=>z.Id==id);item.Width=w;item.Height=h;});}_dragElement=null;_resizing=false;e.Handled=true;}
    void ChoosePresetColor(TextBox target){var colors=new[]{"#FFFFFFFF","#FF38D7A3","#FFFFD166","#FFFF6B6B","#FF4D96FF","#FFB084F5","#FF111827","#FF000000","#CC141D28"};var chooser=new Window{Title="Choose color",Width=340,Height=190,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#171C25")};var panel=new WrapPanel{Margin=new Thickness(10)};foreach(var color in colors){var button=new Button{Width=52,Height=42,Margin=new Thickness(4),Background=Brush(color),ToolTip=color};button.Click+=(_,_)=>{target.Text=color;chooser.DialogResult=true;chooser.Close();};panel.Children.Add(button);}chooser.Content=panel;chooser.ShowDialog();}
    void TextColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(ColorInput);
    void BackgroundColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(BackgroundInput);
    void Fit_Click(object sender,RoutedEventArgs e){if(CanvasFrame.ActualWidth<10||CanvasFrame.ActualHeight<10)return;var scale=Math.Min((CanvasFrame.ActualWidth-8)/1280,(CanvasFrame.ActualHeight-8)/720);CanvasScale.ScaleX=CanvasScale.ScaleY=Math.Clamp(scale,.2,1);}
    void Done_Click(object sender,RoutedEventArgs e)=>Close();
    protected override void OnClosed(EventArgs e){_propertySave.Stop();SaveProperties();_catalog.Changed-=Render;_engine.Changed-=Render;base.OnClosed(e);}
    sealed record LayerItem(ScorebugElement Element){public override string ToString()=>$"{(Element.Visible?"◉":"○")}  {Element.Name}";}
}
