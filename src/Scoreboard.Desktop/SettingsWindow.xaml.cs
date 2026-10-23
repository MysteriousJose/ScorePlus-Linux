using System.Windows;
using System.Windows.Controls;
namespace Scoreboard.Desktop;
public partial class SettingsWindow : Window
{
 readonly UserPreferences _prefs;
 public SettingsWindow(UserPreferences prefs){InitializeComponent();_prefs=prefs;ThemeSelector.ItemsSource=new[]{"Dark","Light"};ThemeSelector.SelectedItem=prefs.Theme;SeparateControls.IsChecked=prefs.SeparateControls;}
 void Theme_Changed(object sender,SelectionChangedEventArgs e){if(ThemeSelector.SelectedItem is string theme){_prefs.Theme=theme;PreferencesStore.Save(_prefs);}}
 void Controls_Changed(object sender,RoutedEventArgs e){if(SeparateControls is not null){_prefs.SeparateControls=SeparateControls.IsChecked==true;PreferencesStore.Save(_prefs);}}
 void Done_Click(object sender,RoutedEventArgs e)=>Close();
}
