using System.Globalization;
using System.IO.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Scoreboard.Desktop;

/// <summary>
/// The native hardware desk: choose a controller and transport, enter the serial or bridge
/// connection details, connect, and watch the raw feed. The window edits a draft and only writes it
/// to preferences when the operator saves or connects, so Cancel leaves a live connection alone.
/// </summary>
public sealed class HardwareWindow : Window
{
    readonly UserPreferences _preferences;
    readonly HardwareService _service;
    readonly HardwareSettings _draft;
    readonly ComboBox _device = new();
    readonly ComboBox _transport = new();
    readonly ComboBox _serialPort = new() { IsEditable = true };
    readonly TextBox _baud = new();
    readonly TextBox _mqttHost = new();
    readonly TextBox _mqttPort = new();
    readonly TextBox _mqttTopic = new();
    readonly TextBox _mqttUser = new();
    readonly PasswordBox _mqttPassword = new();
    readonly TextBox _webSocketUrl = new();
    readonly PasswordBox _webSocketToken = new();
    readonly CheckBox _autoConnect = new() { Content = "Connect automatically when ScorePlus starts" };
    readonly CheckBox _mqttTls = new() { Content = "Use TLS (cloud brokers)" };
    readonly Border _serialPanel = new();
    readonly Border _mqttPanel = new();
    readonly Border _webSocketPanel = new();
    readonly TextBlock _notes = new();
    readonly TextBlock _status = new();
    readonly TextBox _log = new();
    bool _loading = true;

    public HardwareWindow(UserPreferences preferences, HardwareService service)
    {
        _preferences = preferences;
        _service = service;
        _draft = preferences.Hardware.Clone();
        Title = "ScorePlus — Hardware";
        Width = 740;
        Height = 840;
        MinWidth = 640;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#171C25"));
        Foreground = new SolidColorBrush(Color.Parse("#F4F7FB"));
        FontFamily = new FontFamily("Segoe UI");

        _device.ItemsSource = HardwareDevices.All;
        _device.MinHeight = 34;
        _device.Margin = new Thickness(4, 0, 4, 0);
        _device.SelectionChanged += (_, _) => DeviceChanged();
        _transport.ItemsSource = HardwareTransports.All;
        _transport.MinHeight = 34;
        _transport.Margin = new Thickness(4, 0, 4, 0);
        _transport.SelectionChanged += (_, _) => TransportChanged();

        BuildTransportPanels();
        Content = BuildContent();
        RefreshPorts();
        LoadFields();
        _service.Status += ServiceStatus;
        _service.Raw += ServiceRaw;
        Closed += (_, _) =>
        {
            _service.Status -= ServiceStatus;
            _service.Raw -= ServiceRaw;
        };
        _status.Text = _service.IsRunning ? "Connected. ScorePlus is following the controller." : "Not connected.";
        ThemeManager.Apply(this, preferences.Theme);
    }

    Control BuildContent()
    {
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        buttons.Children.Add(Button("Disconnect", (_, _) => { _service.Stop(); _status.Text = "Disconnected."; }));
        buttons.Children.Add(Button("Save", (_, _) => { Commit(); _status.Text = "Hardware settings saved."; }));
        buttons.Children.Add(Button("Close", (_, _) => Close()));
        footer.Children.Add(new TextBlock
        {
            Text = "Hardware input is read-only: ScorePlus follows the controller while it is connected.",
            Foreground = Muted,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = "HARDWARE", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
        stack.Children.Add(Caption("Read live score, clock, period, timeouts and sport data from a scoreboard controller. Daktronics All Sport 5000 is decoded natively; the other consoles use a Sportzcast or Scorebird bridge when their native format is not public."));
        stack.Children.Add(Section("CONTROLLER"));
        stack.Children.Add(_device);
        _notes.Foreground = Muted;
        _notes.FontSize = 11;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.Margin = new Thickness(6, 6, 6, 4);
        stack.Children.Add(_notes);
        stack.Children.Add(Section("DATA SOURCE"));
        stack.Children.Add(_transport);
        _autoConnect.Margin = new Thickness(4, 10, 4, 4);
        stack.Children.Add(_autoConnect);
        stack.Children.Add(Caption("Raw capture is a diagnostic mode. It logs bytes for reverse engineering and never changes the game; use a Sportzcast bridge for live data from controllers whose native format is not published."));
        stack.Children.Add(_serialPanel);
        stack.Children.Add(_mqttPanel);
        stack.Children.Add(_webSocketPanel);

        stack.Children.Add(Section("CONNECTION"));
        var connection = new Border
        {
            Background = Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 2, 0, 0),
            Child = new StackPanel
            {
                Children =
                {
                    _status,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Margin = new Thickness(0, 8, 0, 0),
                        Children =
                        {
                            Button("Connect", (_, _) => { Commit(); _service.Start(_preferences.Hardware); _status.Text = "Connecting…"; }),
                            Button("Disconnect", (_, _) => { _service.Stop(); _status.Text = "Disconnected."; })
                        }
                    }
                }
            }
        };
        _status.Foreground = new SolidColorBrush(Color.Parse("#38D7A3"));
        _status.FontSize = 12;
        _status.FontWeight = FontWeights.SemiBold;
        _status.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(connection);

        stack.Children.Add(Section("RAW LOG"));
        _log.IsReadOnly = true;
        _log.AcceptsReturn = true;
        _log.TextWrapping = TextWrapping.NoWrap;
        _log.Height = 120;
        _log.FontFamily = new FontFamily("Consolas,monospace");
        _log.FontSize = 10;
        _log.Background = new SolidColorBrush(Color.Parse("#050505"));
        _log.Foreground = new SolidColorBrush(Color.Parse("#B7F5E0"));
        _log.Padding = new Thickness(6);
        _log.Margin = new Thickness(4, 0, 4, 5);
        stack.Children.Add(_log);

        return new DockPanel
        {
            Margin = new Thickness(22),
            Children =
            {
                footer,
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = stack
                }
            }
        };
    }

    void BuildTransportPanels()
    {
        var serialStack = new StackPanel { Spacing = 3 };
        serialStack.Children.Add(Label("SERIAL PORT"));
        var serialRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _serialPort.MinHeight = 32;
        _serialPort.Margin = new Thickness(4, 0, 4, 0);
        serialRow.Children.Add(_serialPort);
        var refresh = Button("Refresh", (_, _) => RefreshPorts());
        Grid.SetColumn(refresh, 1);
        refresh.Margin = new Thickness(4, 0, 4, 0);
        serialRow.Children.Add(refresh);
        serialStack.Children.Add(serialRow);
        serialStack.Children.Add(Label("Baud rate"));
        _baud.MinHeight = 32;
        _baud.Padding = new Thickness(8, 4, 8, 4);
        _baud.Margin = new Thickness(4, 0, 4, 0);
        _baud.Width = 130;
        _baud.HorizontalAlignment = HorizontalAlignment.Left;
        serialStack.Children.Add(_baud);
        serialStack.Children.Add(Caption("8 data bits, no parity, 1 stop bit, no flow control. Daktronics uses 19200; OES ISC 9000 uses 115200."));
        _serialPanel.Child = serialStack;
        StylePanel(_serialPanel);

        var mqttStack = new StackPanel { Spacing = 3 };
        mqttStack.Children.Add(Label("SPORTZCAST MQTT"));
        mqttStack.Children.Add(Label("Cloud broker"));
        var brokerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _mqttHost.MinHeight = 32;
        _mqttPort.Width = 88;
        _mqttPort.MinHeight = 32;
        _mqttHost.Margin = new Thickness(4, 0, 4, 0);
        _mqttPort.Margin = new Thickness(4, 0, 4, 0);
        brokerRow.Children.Add(_mqttHost);
        Grid.SetColumn(_mqttPort, 1);
        brokerRow.Children.Add(_mqttPort);
        mqttStack.Children.Add(brokerRow);
        _mqttTls.Margin = new Thickness(4, 8, 4, 2);
        mqttStack.Children.Add(_mqttTls);
        mqttStack.Children.Add(Label("Topic"));
        _mqttTopic.MinHeight = 32;
        _mqttTopic.Margin = new Thickness(4, 0, 4, 0);
        mqttStack.Children.Add(_mqttTopic);
        mqttStack.Children.Add(Label("Username (your Sportzcast email)"));
        _mqttUser.MinHeight = 32;
        _mqttUser.Margin = new Thickness(4, 0, 4, 0);
        mqttStack.Children.Add(_mqttUser);
        mqttStack.Children.Add(Label("Access token / password"));
        _mqttPassword.MinHeight = 32;
        _mqttPassword.Margin = new Thickness(4, 0, 4, 0);
        mqttStack.Children.Add(_mqttPassword);
        mqttStack.Children.Add(Caption("Sportzcast clients subscribe only and QoS is 0, so occasional messages may be lost. For a local ScoreLink+ or ScoreConnect III broker use port 1883 and leave TLS off."));
        _mqttPanel.Child = mqttStack;
        StylePanel(_mqttPanel);

        var wsStack = new StackPanel { Spacing = 3 };
        wsStack.Children.Add(Label("SCOREBIRD NEST WEBSOCKET"));
        wsStack.Children.Add(Label("WebSocket URL"));
        _webSocketUrl.MinHeight = 32;
        _webSocketUrl.Margin = new Thickness(4, 0, 4, 0);
        wsStack.Children.Add(_webSocketUrl);
        wsStack.Children.Add(Label("API token (optional)"));
        _webSocketToken.MinHeight = 32;
        _webSocketToken.Margin = new Thickness(4, 0, 4, 0);
        wsStack.Children.Add(_webSocketToken);
        wsStack.Children.Add(Caption("Scorebird publishes its API/overlay schema only to authorised customers. The URL can contain the token as a query parameter; otherwise it is sent as a Bearer token. ScorePlus reads common JSON aliases for score, clock and period."));
        _webSocketPanel.Child = wsStack;
        StylePanel(_webSocketPanel);
    }

    static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#93A0B2"));
    static readonly IBrush Surface = new SolidColorBrush(Color.Parse("#111721"));

    static void StylePanel(Border border)
    {
        border.Background = Surface;
        border.CornerRadius = new CornerRadius(8);
        border.Padding = new Thickness(8);
        border.Margin = new Thickness(0, 6, 0, 0);
    }

    static TextBlock Section(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(4, 16, 0, 2)
    };

    static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 11,
        Margin = new Thickness(4, 6, 0, 2)
    };

    static TextBlock Caption(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(6, 2, 6, 4)
    };

    static Button Button(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button { Content = text, MinHeight = 32, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0) };
        button.Click += click;
        return button;
    }

    void LoadFields()
    {
        _loading = true;
        try
        {
            _device.SelectedItem = HardwareDevices.Find(_draft.DeviceId);
            _transport.SelectedItem = HardwareTransports.All.FirstOrDefault(option =>
                string.Equals(option.Value, _draft.Transport, StringComparison.OrdinalIgnoreCase))
                ?? HardwareTransports.All[0];
            _autoConnect.IsChecked = _draft.AutoConnect;
            _baud.Text = _draft.BaudRate.ToString(CultureInfo.InvariantCulture);
            _mqttHost.Text = _draft.MqttHost;
            _mqttPort.Text = _draft.MqttPort.ToString(CultureInfo.InvariantCulture);
            _mqttTls.IsChecked = _draft.MqttTls;
            _mqttTopic.Text = _draft.MqttTopic;
            _mqttUser.Text = _draft.MqttUsername;
            _mqttPassword.Password = _draft.MqttPassword;
            _webSocketUrl.Text = _draft.WebSocketUrl;
            _webSocketToken.Password = _draft.WebSocketToken;
            if (!string.IsNullOrWhiteSpace(_draft.SerialPort) && _serialPort.Items.Contains(_draft.SerialPort))
                _serialPort.SelectedItem = _draft.SerialPort;
            else if (_serialPort.Items.Count > 0)
                _serialPort.SelectedIndex = 0;
            UpdateNotes();
            UpdateVisibility();
        }
        finally { _loading = false; }
    }

    void DeviceChanged()
    {
        if (_loading || _device.SelectedItem is not HardwareDeviceProfile profile) return;
        _draft.ApplyProfile(profile);
        LoadFields();
    }

    void TransportChanged()
    {
        if (_loading || _transport.SelectedItem is not HardwareTransportOption option) return;
        _draft.Transport = option.Value;
        UpdateVisibility();
    }

    void UpdateNotes()
    {
        if (_device.SelectedItem is not HardwareDeviceProfile profile) return;
        _notes.Text = profile.Summary + "\n" + profile.Notes;
    }

    void UpdateVisibility()
    {
        var source = _transport.SelectedItem is HardwareTransportOption option
            ? option.Value
            : nameof(HardwareTransport.DaktronicsRtd);
        _serialPanel.IsVisible = source is nameof(HardwareTransport.DaktronicsRtd) or nameof(HardwareTransport.SerialCapture);
        _mqttPanel.IsVisible = source == nameof(HardwareTransport.SportzcastMqtt);
        _webSocketPanel.IsVisible = source == nameof(HardwareTransport.ScorebirdWebSocket);
    }

    void RefreshPorts()
    {
        var current = _draft.SerialPort;
        if (_serialPort.SelectedItem is string selected) current = selected;
        string[] ports;
        try { ports = SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch { ports = []; }
        _serialPort.ItemsSource = ports;
        if (!string.IsNullOrWhiteSpace(current) && ports.Contains(current, StringComparer.OrdinalIgnoreCase))
            _serialPort.SelectedItem = ports.First(port => string.Equals(port, current, StringComparison.OrdinalIgnoreCase));
        else if (ports.Length > 0) _serialPort.SelectedIndex = 0;
    }

    void Commit()
    {
        if (_device.SelectedItem is HardwareDeviceProfile profile) _draft.DeviceId = profile.Id;
        if (_transport.SelectedItem is HardwareTransportOption option) _draft.Transport = option.Value;
        _draft.SerialPort = (_serialPort.Text ?? "").Trim();
        if (int.TryParse(_baud.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var baud))
            _draft.BaudRate = Math.Clamp(baud, 300, 921600);
        _draft.AutoConnect = _autoConnect.IsChecked == true;
        _draft.MqttHost = (_mqttHost.Text ?? "").Trim();
        if (int.TryParse(_mqttPort.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            _draft.MqttPort = Math.Clamp(port, 1, 65535);
        _draft.MqttTls = _mqttTls.IsChecked == true;
        _draft.MqttTopic = (_mqttTopic.Text ?? "").Trim();
        _draft.MqttUsername = (_mqttUser.Text ?? "").Trim();
        _draft.MqttPassword = _mqttPassword.Password;
        _draft.WebSocketUrl = (_webSocketUrl.Text ?? "").Trim();
        _draft.WebSocketToken = _webSocketToken.Password;
        _preferences.Hardware = _draft.Clone();
        PreferencesStore.Save(_preferences);
    }

    void ServiceStatus(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => _status.Text = message); return; }
        _status.Text = message;
    }

    void ServiceRaw(string message) => Dispatcher.BeginInvoke(() =>
    {
        _log.Text = (_log.Text ?? "") + message + Environment.NewLine;
        if ((_log.Text?.Length ?? 0) > 24000) _log.Text = _log.Text![^20000..];
        _log.CaretIndex = _log.Text?.Length ?? 0;
    });
}
