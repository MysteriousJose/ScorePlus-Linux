using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// A detached copy of the control surface. It is deliberately thin: the same
/// <see cref="ControlPanelView"/> used by the main window lives inside it, so the two can
/// never drift apart and every input drives the one shared <see cref="GameEngine"/>.
/// </summary>
public partial class GameControlWindow : Window
{
    readonly GameEngine _engine;

    public ControlPanelView Panel { get; }

    /// <summary>The media dock in this window; every panel shares one session but keeps its own view.</summary>
    public MediaDock Media => MediaControl;

    public event Action? NewPanelRequested;

    public GameControlWindow(GameEngine engine, Action publishOutput, Action<bool> setLiveScoreClock, Func<bool> getLiveScoreClock, MediaSession? media = null)
        : this(engine, new DelegateHost { Publish = publishOutput, SetLive = setLiveScoreClock, GetLive = getLiveScoreClock }, media)
    {
    }

    public GameControlWindow(GameEngine engine, IControlPanelHost host, MediaSession? media = null)
    {
        InitializeComponent();
        _engine = engine;
        if (host is DelegateHost detached) detached.NewPanel = () => NewPanelRequested?.Invoke();
        Panel = new ControlPanelView(engine, host);
        PanelHost.Content = Panel;
        if (media is not null)
        {
            MediaControl.Attach(media);
            media.Status += message => MediaStatus.Text = message;
        }
        ApplyTheme(PreferencesStore.Load().Theme);
    }

    /// <summary>Applies the operator theme to this window and to its media dock.</summary>
    public void ApplyTheme(string theme)
    {
        ThemeManager.Apply(this, theme);
        MediaControl.ApplyTheme(theme);
    }

    public void SyncLiveState(bool live) => Panel.SyncLiveState(live);

    public void Execute(string actionId) => Panel.Execute(actionId);

    public void ApplyKeybinds(KeybindSettings settings) => Panel.ApplyKeybinds(settings);

    void Undo_Click(object sender, RoutedEventArgs e) => Panel.Execute("global.undo");

    void Keybinds_Click(object sender, RoutedEventArgs e) => Panel.ShowKeybindsDialog();

    void NewPanel_Click(object sender, RoutedEventArgs e) => NewPanelRequested?.Invoke();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Panel.TryHandleKey(key, Keyboard.Modifiers)) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>Adapter for callers that only supply publish/live callbacks (also used by tests).</summary>
    sealed class DelegateHost : IControlPanelHost
    {
        public Action Publish = () => { };
        public Action<bool> SetLive = _ => { };
        public Func<bool> GetLive = () => true;
        public Action NewPanel = () => { };

        public void PublishOutput() => Publish();
        public bool LiveScoreClock { get => GetLive(); set => SetLive(value); }
        public bool HasStagedChanges => false;
        public void OpenNewPanel() => NewPanel();
        public string LayoutLabel => "1 · Layout";
        public void ShiftLayout(int delta) { }
    }
}

internal sealed class AddStatDialog : Window
{
    readonly ComboBox _presets = new();
    readonly TextBox _custom = new();

    public string StatName { get; private set; } = "";

    public AddStatDialog(IReadOnlyCollection<string> existing)
    {
        Title = "Add custom stat";
        Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Choose a common stat or type your own", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 0, 0, 6) });
        _presets.ItemsSource = CommonStats.Names.Where(n => !existing.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray();
        _presets.SelectedIndex = 0;
        panel.Children.Add(_presets);
        panel.Children.Add(new TextBlock { Text = "Custom name (overrides the choice above)", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 8, 0, 6) });
        panel.Children.Add(_custom);
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 114)), Margin = new Thickness(4, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var add = new Button { Content = "Add stat", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        add.Click += (_, _) =>
        {
            var name = _custom.Text.Trim();
            if (name.Length == 0) name = (_presets.SelectedItem as string ?? "").Trim();
            if (name.Length == 0) { error.Text = "Choose a stat or type a name."; return; }
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) { error.Text = "That stat already exists."; return; }
            StatName = name;
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(add); buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _custom.Focus();
    }
}

internal sealed class ClockTimeDialog : Window
{
    readonly TextBox _input = new();
    readonly TextBlock _error = new();
    public int Seconds { get; private set; }

    public ClockTimeDialog(string title, int seconds)
    {
        Title = title;
        Width = 360; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;
        Seconds = seconds;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Enter MM:SS (for example 10:30) or a total number of seconds.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 0, 0, 8) });
        _input.Text = ClockFormat.Format(seconds);
        panel.Children.Add(_input);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 114));
        _error.Margin = new Thickness(4, 4, 0, 0);
        _error.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_error);
        var apply = new Button { Content = "Apply", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        apply.Click += (_, _) =>
        {
            if (!ClockFormat.TryParse(_input.Text, out var parsed)) { _error.Text = "Use MM:SS or a whole number of seconds."; return; }
            Seconds = parsed;
            DialogResult = true;
            Close();
        };
        panel.Children.Add(apply);
        Content = panel;
        ThemeManager.Apply(this, PreferencesStore.Load().Theme);
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }
}
