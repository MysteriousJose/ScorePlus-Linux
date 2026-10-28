using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// A detached copy of the control surface. The game, the media library and every audience output
/// are shared, but the display and layout this window is previewing are its own, so two panels can
/// line up two different screens at the same time.
/// </summary>
public partial class GameControlWindow : Window
{
    readonly GameEngine _engine;
    ScorebugCatalog? _catalog;
    ScorebugCanvasView? _preview;
    bool _displaySync;
    bool _showingMedia;

    public ControlPanelView Panel { get; }

    /// <summary>This window's display and staged layouts. Null only in the minimal test host.</summary>
    public ProgramSelection? Program { get; private set; }

    /// <summary>The media dock in this window; every panel shares one session but keeps its own view.</summary>
    public MediaDock Media => MediaControl;

    public event Action? NewPanelRequested;

    public GameControlWindow(GameEngine engine, Action publishOutput, Action<bool> setLiveScoreClock, Func<bool> getLiveScoreClock, MediaSession? media = null)
        : this(engine, new DelegateHost { Publish = publishOutput, SetLive = setLiveScoreClock, GetLive = getLiveScoreClock }, media)
    {
    }

    /// <summary>The minimal host: no catalog, so no program strip. Used by the checks.</summary>
    public GameControlWindow(GameEngine engine, IControlPanelHost host, MediaSession? media = null)
    {
        InitializeComponent();
        _engine = engine;
        if (host is DelegateHost detached) detached.NewPanel = () => NewPanelRequested?.Invoke();
        Panel = new ControlPanelView(engine, host);
        PanelHost.Content = Panel;
        ProgramStrip.Visibility = Visibility.Collapsed;
        AttachMedia(media);
        ApplyTheme(PreferencesStore.Load().Theme);
    }

    /// <summary>
    /// A full panel: it previews and publishes its own display, while the game data and the media
    /// session come from the main window.
    /// </summary>
    public GameControlWindow(GameEngine engine, GameEngine previewEngine, ScorebugCatalog catalog,
        Action publishGameState, Func<bool> hasStagedGame, Action<bool> setLiveScoreClock, Func<bool> getLiveScoreClock,
        MediaSession? media, string theme)
    {
        InitializeComponent();
        _engine = engine;
        _catalog = catalog;
        Program = new ProgramSelection(catalog);
        _preview = new ScorebugCanvasView(catalog, previewEngine, playback: media?.Playback, layoutProvider: () => Program!.Layout);
        PreviewHost.Content = _preview;
        var host = new WindowHost(Program, publishGameState, hasStagedGame, setLiveScoreClock, getLiveScoreClock, () => NewPanelRequested?.Invoke());
        Panel = new ControlPanelView(engine, host);
        PanelHost.Content = Panel;
        Program.Changed += ProgramChanged;
        catalog.Changed += CatalogChanged;
        AttachMedia(media);
        ApplyTheme(theme);
        SyncDisplayPicker();
        ProgramChanged();
        ShowTab(media: false);
    }

    void AttachMedia(MediaSession? media)
    {
        if (media is null) return;
        MediaControl.Attach(media);
        media.Status += message => MediaStatus.Text = message;
    }

    /// <summary>Applies the operator theme to this window and to its media dock.</summary>
    public void ApplyTheme(string theme)
    {
        ThemeManager.Apply(this, theme);
        MediaControl.ApplyTheme(theme);
        // In a tab the dock has the full panel width, so its buttons are the size of the console's.
        MediaControl.SetLargeMode(true);
    }

    public void SyncLiveState(bool live) => Panel.SyncLiveState(live);

    public void Execute(string actionId) => Panel.Execute(actionId);

    public void ApplyKeybinds(KeybindSettings settings) => Panel.ApplyKeybinds(settings);

    // ---------- program ----------

    void CatalogChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(CatalogChanged); return; }
        SyncDisplayPicker();
        ProgramChanged();
    }

    void ProgramChanged()
    {
        if (Program is null) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(ProgramChanged); return; }
        PreviewLayoutLabel.Text = Program.Label;
        var count = Program.Display.Layouts.Count;
        LayoutPrevButton.IsEnabled = LayoutNextButton.IsEnabled = count > 1;
        ProgramHint.Text = Program.HasStagedChanges
            ? "Preview only — press Update Output to put this display on air."
            : "This display is on air with the layout shown here.";
        _preview?.Rebuild();
        Panel.Refresh();
    }

    /// <summary>Fills the display picker without treating the sync as the operator's choice.</summary>
    void SyncDisplayPicker()
    {
        if (_catalog is null || Program is null) return;
        _displaySync = true;
        try
        {
            var names = _catalog.Displays.Select(display => display.Name).ToArray();
            if (DisplaySelector.ItemsSource is not IEnumerable<string> existing || !existing.SequenceEqual(names))
                DisplaySelector.ItemsSource = names;
            var index = _catalog.Displays.FindIndex(display => string.Equals(display.Id, Program.DisplayId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && DisplaySelector.SelectedIndex != index) DisplaySelector.SelectedIndex = index;
        }
        finally { _displaySync = false; }
    }

    void Display_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_displaySync || Program is null || _catalog is null || DisplaySelector.SelectedIndex < 0) return;
        var display = _catalog.Displays[DisplaySelector.SelectedIndex];
        Program.SelectDisplay(display.Id);
    }

    void LayoutPrev_Click(object sender, RoutedEventArgs e) => Program?.Shift(-1);

    void LayoutNext_Click(object sender, RoutedEventArgs e) => Program?.Shift(1);

    // ---------- tabs ----------

    /// <summary>Switches this panel between the console and the media desk.</summary>
    public void ShowMediaTab(bool media) => ShowTab(media);

    void ControlTab_Click(object sender, RoutedEventArgs e) => ShowTab(media: false);

    void MediaTab_Click(object sender, RoutedEventArgs e) => ShowTab(media: true);

    /// <summary>Switches between the console and the media desk; both fill the same pane.</summary>
    void ShowTab(bool media)
    {
        _showingMedia = media;
        PanelHost.Visibility = media ? Visibility.Collapsed : Visibility.Visible;
        MediaControl.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        var on = (Brush)new BrushConverter().ConvertFromString("#123C33")!;
        var off = (Brush)new BrushConverter().ConvertFromString("#141A23")!;
        var onBorder = (Brush)new BrushConverter().ConvertFromString("#38D7A3")!;
        var offBorder = (Brush)new BrushConverter().ConvertFromString("#28323F")!;
        ControlTab.Background = media ? off : on;
        ControlTab.BorderBrush = media ? offBorder : onBorder;
        ControlTab.Foreground = media ? (Brush)new BrushConverter().ConvertFromString("#93A0B2")! : Brushes.White;
        MediaTab.Background = media ? on : off;
        MediaTab.BorderBrush = media ? onBorder : offBorder;
        MediaTab.Foreground = media ? Brushes.White : (Brush)new BrushConverter().ConvertFromString("#93A0B2")!;
        if (media) MediaControl.RefreshView();
    }

    void Undo_Click(object sender, RoutedEventArgs e) => Panel.Execute("global.undo");

    void Keybinds_Click(object sender, RoutedEventArgs e) => Panel.ShowKeybindsDialog();

    void NewPanel_Click(object sender, RoutedEventArgs e) => NewPanelRequested?.Invoke();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Panel.TryHandleKey(key, Keyboard.Modifiers)) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Program is not null)
        {
            Program.Changed -= ProgramChanged;
            Program.Dispose();
        }
        if (_catalog is not null) _catalog.Changed -= CatalogChanged;
        _preview?.Dispose();
        base.OnClosed(e);
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

    /// <summary>
    /// The panel's host: publishing sends the shared game data and then this window's own staged
    /// layouts, so one panel never publishes the layout another panel is holding.
    /// </summary>
    sealed class WindowHost : IControlPanelHost
    {
        readonly ProgramSelection _program;
        readonly Action _publishGameState;
        readonly Func<bool> _hasStagedGame;
        readonly Action<bool> _setLive;
        readonly Func<bool> _getLive;
        readonly Action _newPanel;

        public WindowHost(ProgramSelection program, Action publishGameState, Func<bool> hasStagedGame,
            Action<bool> setLive, Func<bool> getLive, Action newPanel)
        {
            _program = program;
            _publishGameState = publishGameState;
            _hasStagedGame = hasStagedGame;
            _setLive = setLive;
            _getLive = getLive;
            _newPanel = newPanel;
        }

        public void PublishOutput()
        {
            _publishGameState();
            _program.PublishStaged();
        }

        public bool LiveScoreClock { get => _getLive(); set => _setLive(value); }

        public bool HasStagedChanges => _hasStagedGame() || _program.HasStagedChanges;

        public void OpenNewPanel() => _newPanel();

        public string LayoutLabel => _program.Label;

        public void ShiftLayout(int delta) => _program.Shift(delta);
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
