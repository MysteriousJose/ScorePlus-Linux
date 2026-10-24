using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Scoreboard.Desktop;
public sealed class UserPreferences
{
    public string Theme { get; set; } = "Dark";
    public    bool SeparateControls { get; set; }

}
public static class PreferencesStore
{
    static readonly string FilePath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Scoreboard","preferences.json");
    public static UserPreferences Load(){try{return File.Exists(FilePath)?JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(FilePath))??new():new();}catch{return new();}}
    public static void Save(UserPreferences p){Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);var temp=FilePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(p,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,FilePath,true);}
}
public partial class PreferencesWindow : Window
{
    readonly UserPreferences _preferences;
    bool _loading=true;
    public PreferencesWindow(UserPreferences preferences){InitializeComponent();_preferences=preferences;ThemeSelector.ItemsSource=new[]{"Dark","Light"};ThemeSelector.SelectedItem=preferences.Theme;SeparateControls.IsChecked=preferences.SeparateControls;_loading=false;}
    void Theme_Changed(object sender,SelectionChangedEventArgs e){if(!_loading&&ThemeSelector.SelectedItem is string theme)_preferences.Theme=theme;}
    void Controls_Changed(object sender,RoutedEventArgs e){if(!_loading&&SeparateControls is not null)_preferences.SeparateControls=SeparateControls.IsChecked==true;}
    void Save_Click(object sender,RoutedEventArgs e){PreferencesStore.Save(_preferences);DialogResult=true;Close();}
    void Done_Click(object sender,RoutedEventArgs e)=>Close();
}
public static class ThemeManager
{
    public static void Apply(Window window,string theme)
    {
        var light=theme=="Light";var background=light?"#FFF3F5F8":"#FF000000";var panel=light?"#FFFFFFFF":"#FF080808";var surface=light?"#FFE5EAF0":"#FF141414";var foreground=light?"#FF18202B":"#FFF4F7FB";var muted=light?"#FF586576":"#FFB0B0B0";var input=light?"#FFFFFFFF":"#FF050505";var border=light?"#FFB9C3CE":"#FF383838";
        window.Background=Brush(background);window.Foreground=Brush(foreground);
        void Apply(){SetDescendants(window.Content as DependencyObject,panel,surface,foreground,muted,input,border);}
        window.Loaded+=(_,_)=>Apply();
        if(window.IsLoaded)window.Dispatcher.BeginInvoke(Apply);
    }
    static Brush Brush(string color)=>(Brush)new BrushConverter().ConvertFromString(color)!;
    static void SetDescendants(DependencyObject? root,string panel,string surface,string foreground,string muted,string input,string border)
    {
        if(root is null)return;
        if(root is Border b && b.Background is SolidColorBrush) b.Background=Brush(panel);
        if(root is Panel p && p.Background is SolidColorBrush) p.Background=Brush(panel);
        if(root is TextBlock t && t.Foreground is SolidColorBrush old && old.Color!=Color.FromRgb(56,215,163) && old.Color!=Color.FromRgb(255,209,102) && old.Color!=Color.FromRgb(255,107,107))t.Foreground=Brush(foreground);
        if(root is ComboBoxItem comboItem){comboItem.Foreground=Brush(foreground);comboItem.Background=Brush(surface);}
        if(root is Control c)
        {
            c.Foreground=Brush(foreground);
            if(c is TextBox){c.Background=Brush(input);c.BorderBrush=Brush(border);}
            else if(c is Button){c.Background=Brush(surface);c.BorderBrush=Brush(border);}
            else if(c is ListBox){c.Background=Brush(input);c.BorderBrush=Brush(border);}
            else if(c is ComboBox){c.Background=Brush(input);c.BorderBrush=Brush(border);c.Foreground=Brush(foreground);}
        }
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)SetDescendants(System.Windows.Media.VisualTreeHelper.GetChild(root,i),panel,surface,foreground,muted,input,border);
    }
}
