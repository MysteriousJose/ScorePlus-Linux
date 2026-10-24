using System.Windows;
using System.Windows.Controls;
namespace Scoreboard.Desktop;
public partial class SettingsWindow : Window
{
 readonly UserPreferences _prefs;
 bool _loading=true;
 public SettingsWindow(UserPreferences prefs){InitializeComponent();_prefs=prefs;ThemeSelector.ItemsSource=new[]{"Dark","Light"};ThemeSelector.SelectedItem=prefs.Theme;SeparateControls.IsChecked=prefs.SeparateControls;_loading=false;}
 void Theme_Changed(object sender,SelectionChangedEventArgs e){if(!_loading&&ThemeSelector.SelectedItem is string theme)_prefs.Theme=theme;}
 void Controls_Changed(object sender,RoutedEventArgs e){if(!_loading&&SeparateControls is not null)_prefs.SeparateControls=SeparateControls.IsChecked==true;}
 void Save_Click(object sender,RoutedEventArgs e){PreferencesStore.Save(_prefs);DialogResult=true;Close();}
 void Done_Click(object sender,RoutedEventArgs e)=>Close();
}
