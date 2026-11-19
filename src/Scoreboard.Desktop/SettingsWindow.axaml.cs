using System.Globalization;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Input;
using Scoreboard.Core;
namespace Scoreboard.Desktop;
public partial class SettingsWindow : Window
{
    /// <summary>The longest switch delay the graphic accepts, in seconds.</summary>
    const double MaxDelay = 5;

    readonly UserPreferences _original;
    readonly UserPreferences _draft;
    bool _loading = true;
    bool _deleteArmed;
    /// <summary>Set once a value was saved without closing, so closing later keeps it.</summary>
    bool _committed;

    /// <summary>True when the page stored its draft, so the owner may apply the new preferences.</summary>
    public bool Committed => _committed;

    public SettingsWindow(UserPreferences prefs)
    {
        InitializeComponent();
        // Enter while typing in a preference field must settle that field, not fire the window's
        // default Save button. Avalonia has no PreviewKeyDown event, so the tunnel route of the
        // KeyDown routed event is the same moment WPF's preview handler fired.
        AddHandler(Avalonia.Input.InputElement.KeyDownEvent, new EventHandler<Avalonia.Input.KeyEventArgs>(InterceptEnterKey), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _original = prefs;
        _draft = prefs.Clone();
        ThemeSelector.ItemsSource = new[] { "System", "Dark", "Light" };
        ThemeSelector.SelectedItem = ThemeSelector.Items.Contains(_draft.Theme) ? _draft.Theme : "System";
        ShowHints.IsChecked = _draft.ShowHeadingHints;
        QuickPublishInput.IsChecked = _draft.QuickLayoutsPublish;
        DownPreviewInput.IsChecked = _draft.NextDownPreview;
        NewSeriesInput.IsChecked = _draft.NewSeriesLive;
        ReplaySecondsInput.Text = Math.Clamp(_draft.ReplaySeconds, 10, 900).ToString(CultureInfo.InvariantCulture);
        GameClockHornInput.IsChecked = _draft.HornOnGameClockZero;
        PlayClockHornInput.IsChecked = _draft.HornOnPlayClockZero;
        AutosaveMinutesInput.Text = _draft.AutosaveMinutes.ToString(CultureInfo.InvariantCulture);
        AutoRecordInput.IsChecked = _draft.AutoUpdateRecord;
        TieRuleInput.SelectedIndex = string.Equals(_draft.TieRule, "AllowTie", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        TiePeriodInput.Text = _draft.TieAllowPeriod.ToString(CultureInfo.InvariantCulture);
        TiePeriodInput.IsEnabled = TieRuleInput.SelectedIndex == 1;
        OvertimeFinalInput.IsChecked = _draft.ShowOvertimeFinal;
        DelayInput.Text = _draft.ValueSwitchDelay.ToString("0.##", CultureInfo.CurrentCulture);
        AccentInput.Text = _draft.AccentColor;
        Accent2Input.Text = _draft.AccentColor2;
        PaintSwatches();
        RemoteEnabledInput.IsChecked = _draft.RemoteEnabled;
        RemotePasswordInput.Password = _draft.RemotePassword;
        RemotePortInput.Text = _draft.RemotePort.ToString(CultureInfo.InvariantCulture);
        CheckUpdatesInput.IsChecked = _draft.CheckForUpdates;
        UpdateFeedInput.Text = _draft.UpdateFeedUrl;
        _loading = false;
        UpdateAutosaveStatus();
        UpdateLockStatus();
        UpdateRemoteStatus();
        UpdateStatus.Text = AppInfo.Kind switch
        {
            InstallKind.Tarball => $"Installed build {AppInfo.Release} as a per-user tarball. Updates are checked at launch and installed with install.sh.",
            InstallKind.AppImage => $"Running as an AppImage ({AppInfo.Release}). Updates replace the AppImage file in place.",
            InstallKind.SystemPackage => $"Installed build {AppInfo.Release} as a .deb. Updates download the package and install it through the package manager.",
            _ => "This copy runs from a folder, so it does not update itself. Install it from the tarball, .deb or AppImage to enable updates.",
        };
    }

    // ---------- interface colours ----------

    void PaintSwatches()
    {
        AccentSwatch.Background = new Avalonia.Media.SolidColorBrush(UiPalette.Parse(_draft.AccentColor, Avalonia.Media.Color.FromRgb(0x38, 0xD7, 0xA3)));
        Accent2Swatch.Background = new Avalonia.Media.SolidColorBrush(UiPalette.Parse(_draft.AccentColor2, Avalonia.Media.Color.FromRgb(0x0B, 0x6B, 0x50)));
    }

    /// <summary>A typed hex value is used as soon as it is a real colour; half-typed text waits.</summary>
    void Accent_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || AccentInput is null || Accent2Input is null) return;
        if (IsColor(AccentInput.Text)) _draft.AccentColor = AccentInput.Text?.Trim() ?? "";
        if (IsColor(Accent2Input.Text)) _draft.AccentColor2 = Accent2Input.Text?.Trim() ?? "";
        PaintSwatches();
    }

    static bool IsColor(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length < 4 || value[0] != '#') return false;
        var digits = value[1..];
        return digits.Length is 3 or 4 or 6 or 8 && digits.All(Uri.IsHexDigit);
    }

    void AccentPick_Click(object sender, RoutedEventArgs e)
    {
        if (ColorPickerWindow.Pick(this, _draft.AccentColor) is not string picked) return;
        _draft.AccentColor = picked;
        AccentInput.Text = picked;
        PaintSwatches();
        ThemeManager.Apply(this, _draft.Theme);
    }

    void Accent2Pick_Click(object sender, RoutedEventArgs e)
    {
        if (ColorPickerWindow.Pick(this, _draft.AccentColor2) is not string picked) return;
        _draft.AccentColor2 = picked;
        Accent2Input.Text = picked;
        PaintSwatches();
        ThemeManager.Apply(this, _draft.Theme);
    }

    void ResetColors_Click(object sender, RoutedEventArgs e)
    {
        _draft.AccentColor = "#FF38D7A3";
        _draft.AccentColor2 = "#FF0B6B50";
        AccentInput.Text = _draft.AccentColor;
        Accent2Input.Text = _draft.AccentColor2;
        PaintSwatches();
        ThemeManager.Apply(this, _draft.Theme);
    }

    // ---------- remote control ----------

    void Remote_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || RemoteEnabledInput is null) return;
        _draft.RemoteEnabled = RemoteEnabledInput.IsChecked == true;
        _draft.RemotePassword = RemotePasswordInput.Password;
        if (int.TryParse(RemotePortInput.Text?.Trim() ?? "", out var port) && port is >= 1024 and <= 65535) _draft.RemotePort = port;
        UpdateRemoteStatus();
    }

    void UpdateRemoteStatus()
    {
        if (!_draft.RemoteEnabled)
        {
            RemoteStatus.Text = "Remote is off. Tick the box and set a password to serve it.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_draft.RemotePassword))
        {
            RemoteStatus.Text = "Add a password — the remote stays off until it has one.";
            return;
        }
        var urls = RemoteControlServer.Urls(_draft.RemotePort);
        // The network address is the one a phone types; localhost is listed too, for this computer.
        var lan = urls.Count > 1 ? urls[^1] : urls[0];
        RemoteStatus.Text = $"Ready on port {_draft.RemotePort}. On this computer http://localhost:{_draft.RemotePort} · on a phone on the same Wi-Fi {lan}";
    }

    void OpenRemote_Click(object sender, RoutedEventArgs e)
    {
        // The address on the network, not localhost: the operator usually copies it from the URL
        // bar onto a phone, and a localhost URL is useless there.
        var urls = RemoteControlServer.Urls(_draft.RemotePort);
        var url = urls.Count > 1 ? urls[^1] : urls[0];
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { RemoteStatus.Text = $"Could not open a browser: {ex.Message}"; }
    }

    // ---------- updates ----------

    void Update_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || CheckUpdatesInput is null || UpdateFeedInput is null) return;
        _draft.CheckForUpdates = CheckUpdatesInput.IsChecked == true;
        _draft.UpdateFeedUrl = UpdateFeedInput.Text?.Trim() ?? "";
    }

    async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!AppInfo.CanUpdate)
        {
            UpdateStatus.Text = "This copy runs from a folder, so it does not update itself. Install it from the tarball, .deb or AppImage, then updates work here.";
            return;
        }
        var feed = UpdateFeed.Resolve(UpdateFeedInput.Text);
        UpdateStatus.Text = string.IsNullOrWhiteSpace(UpdateFeedInput.Text)
            ? "Checking the built-in feed…"
            : "Checking…";
        var manifest = await UpdateChecker.CheckAsync(feed, AppInfo.Kind);
        UpdateStatus.Text = manifest is null
            ? $"You are on the newest version ({AppInfo.Version})."
            : $"Version {manifest.Version} is ready. {(string.IsNullOrWhiteSpace(manifest.Notes) ? "" : manifest.Notes)}";
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeSelector.SelectedItem is string theme) _draft.Theme = theme;
    }

    void DownPreview_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || DownPreviewInput is null) return;
        _draft.NextDownPreview = DownPreviewInput.IsChecked == true;
    }

    void NewSeries_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || NewSeriesInput is null) return;
        _draft.NewSeriesLive = NewSeriesInput.IsChecked == true;
    }

    void ClockHorn_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || GameClockHornInput is null || PlayClockHornInput is null) return;
        _draft.HornOnGameClockZero = GameClockHornInput.IsChecked == true;
        _draft.HornOnPlayClockZero = PlayClockHornInput.IsChecked == true;
    }

    void ReplaySeconds_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || ReplaySecondsInput is null) return;
        if (int.TryParse(ReplaySecondsInput.Text?.Trim() ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            _draft.ReplaySeconds = Math.Clamp(seconds, 10, 900);
    }

    void AutoRecord_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || AutoRecordInput is null) return;
        _draft.AutoUpdateRecord = AutoRecordInput.IsChecked == true;
    }

    void TieRule_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TieRuleInput is null || TiePeriodInput is null) return;
        _draft.TieRule = TieRuleInput.SelectedIndex == 1 ? "AllowTie" : "PlayUntilWinner";
        TiePeriodInput.IsEnabled = TieRuleInput.SelectedIndex == 1;
    }

    void TiePeriod_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || TiePeriodInput is null) return;
        if (int.TryParse(TiePeriodInput.Text?.Trim() ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out var period))
            _draft.TieAllowPeriod = Math.Clamp(period, 1, 99);
    }

    void OvertimeFinal_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || OvertimeFinalInput is null) return;
        _draft.ShowOvertimeFinal = OvertimeFinalInput.IsChecked == true;
    }

    void Autosave_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || AutosaveMinutesInput is null) return;
        if (!int.TryParse(AutosaveMinutesInput.Text?.Trim() ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) return;
        _draft.AutosaveMinutes = Math.Max(0, minutes);
        UpdateAutosaveStatus();
    }

    void UpdateAutosaveStatus()
    {
        if (AutosaveStatus is null) return;
        var minutes = _draft.AutosaveMinutes;
        AutosaveStatus.Text = minutes <= 0
            ? "Autosave is off. Existing recovery points are kept."
            : minutes == 1
                ? "A recovery point is written every minute while you work."
                : $"A recovery point is written every {minutes} minutes while you work.";
    }

    // ---------- operator lock ----------

    void Lock_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || OperatorLockPasswordInput is null || OperatorLockConfirmInput is null) return;
        var clearing = ClearOperatorLockInput.IsChecked == true;
        OperatorLockPasswordInput.IsEnabled = !clearing;
        OperatorLockConfirmInput.IsEnabled = !clearing;
        UpdateLockStatus();
    }

    void UpdateLockStatus()
    {
        if (OperatorLockStatus is null) return;
        if (ClearOperatorLockInput.IsChecked == true)
        {
            OperatorLockStatus.Text = "The lock password will be removed when you save.";
            return;
        }
        OperatorLockStatus.Text = string.IsNullOrWhiteSpace(_draft.OperatorLockPasswordHash)
            ? "No lock password is set. Type one in both boxes to enable the lock."
            : "A lock password is set. Type a new one in both boxes to change it, or leave both empty to keep it.";
    }

    /// <summary>Validates the lock fields and advances the draft only when they make sense.</summary>
    bool ApplyLockPassword()
    {
        if (ClearOperatorLockInput.IsChecked == true)
        {
            _draft.OperatorLockPasswordHash = "";
            return true;
        }
        var password = OperatorLockPasswordInput.Password;
        var confirm = OperatorLockConfirmInput.Password;
        if (string.IsNullOrWhiteSpace(password)) return true; // keep the existing password
        if (password != confirm)
        {
            OperatorLockStatus.Text = "The two passwords do not match.";
            return false;
        }
        _draft.OperatorLockPasswordHash = OperatorLock.Hash(password);
        return true;
    }

    void Controls_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || ShowHints is null) return;
        _draft.ShowHeadingHints = ShowHints.IsChecked == true;
    }

    /// <summary>A quick button either stages or publishes; the choice is saved with everything else.</summary>
    void QuickLayout_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || QuickPublishInput is null) return;
        _draft.QuickLayoutsPublish = QuickPublishInput.IsChecked == true;
    }

    /// <summary>
    /// Any value the operator types is used as-is inside 0–5, so 0.35 s is a real answer rather
    /// than the nearest item in a fixed list. Half-typed text ("", "0.") leaves the draft alone.
    /// </summary>
    void Delay_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || DelayInput is null) return;
        if (!double.TryParse(DelayInput.Text?.Trim() ?? "", NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds)) return;
        _draft.ValueSwitchDelay = Math.Clamp(seconds, 0, MaxDelay);
    }

    /// <summary>Clears every recovery point; a second click confirms, so one tap cannot wipe them.</summary>
    void DeleteAutosaves_Click(object sender, RoutedEventArgs e)
    {
        var remaining = AutosaveStore.List().Count;
        if (remaining == 0)
        {
            AutosaveStatus.Text = "There are no autosaves to delete.";
            DeleteAutosavesButton.Content = "Delete all autosaves";
            _deleteArmed = false;
            return;
        }
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            DeleteAutosavesButton.Content = $"Delete {remaining} autosave{(remaining == 1 ? "" : "s")}?";
            AutosaveStatus.Text = "Click again to remove every autosave on this computer.";
            return;
        }
        var deleted = AutosaveStore.DeleteAll();
        _deleteArmed = false;
        DeleteAutosavesButton.Content = "Delete all autosaves";
        AutosaveStatus.Text = deleted == 0
            ? "No autosaves could be deleted."
            : $"Deleted {deleted} autosave{(deleted == 1 ? "" : "s")}. Saved .spg games are untouched.";
    }

    /// <summary>
    /// Enter while typing in a preference field saves what was typed and keeps the page open.
    /// Save is the window's default button, so without this a stray Enter mid-edit saved and
    /// dismissed the whole page. Enter anywhere else still reaches its own control.
    /// </summary>
    void InterceptEnterKey(object? sender, KeyEventArgs e)
    {
        // Avalonia reports the keystroke source as Source (WPF's OriginalSource).
        if (e.Key != Key.Enter || e.Source is not (TextBox or PasswordBox)) return;
        e.Handled = true;
        if (!Commit()) return;
        _committed = true;
        // Show the value that was actually stored, so "0.50" or "7" read back as what stuck.
        if (DelayInput is not null)
            DelayInput.Text = _draft.ValueSwitchDelay.ToString("0.##", CultureInfo.CurrentCulture);
        if (AutosaveMinutesInput is not null)
            AutosaveMinutesInput.Text = _draft.AutosaveMinutes.ToString(CultureInfo.InvariantCulture);
        // The page was painted with the theme it opened with; follow a theme picked this way.
        ThemeManager.Apply(this, _draft.Theme);
    }

    /// <summary>Writes the draft over the live preferences and stores them.</summary>
    bool Commit()
    {
        if (!ApplyLockPassword()) return false;
        _original.Theme = _draft.Theme;
        _original.ShowHeadingHints = _draft.ShowHeadingHints;
        _original.QuickLayoutsPublish = _draft.QuickLayoutsPublish;
        _original.NextDownPreview = _draft.NextDownPreview;
        _original.NewSeriesLive = _draft.NewSeriesLive;
        _original.HornOnGameClockZero = _draft.HornOnGameClockZero;
        _original.HornOnPlayClockZero = _draft.HornOnPlayClockZero;
        _original.ValueSwitchDelay = Math.Clamp(_draft.ValueSwitchDelay, 0, MaxDelay);
        _original.AutosaveMinutes = Math.Max(0, _draft.AutosaveMinutes);
        _original.ReplaySeconds = Math.Clamp(_draft.ReplaySeconds, 10, 900);
        CameraFeed.ReplayWindowSeconds = _original.ReplaySeconds;
        _original.AutoUpdateRecord = _draft.AutoUpdateRecord;
        _original.TieRule = _draft.TieRule;
        _original.TieAllowPeriod = Math.Clamp(_draft.TieAllowPeriod, 1, 99);
        _original.ShowOvertimeFinal = _draft.ShowOvertimeFinal;
        _original.AccentColor = _draft.AccentColor;
        _original.AccentColor2 = _draft.AccentColor2;
        _original.RemotePort = _draft.RemotePort;
        _original.RemotePassword = _draft.RemotePassword;
        _original.OperatorLockPasswordHash = _draft.OperatorLockPasswordHash;
        // A remote with no password is refused rather than served openly on the network.
        _original.RemoteEnabled = _draft.RemoteEnabled && !string.IsNullOrWhiteSpace(_draft.RemotePassword);
        _original.CheckForUpdates = _draft.CheckForUpdates;
        _original.UpdateFeedUrl = _draft.UpdateFeedUrl;
        PreferencesStore.Save(_original);
        GameEngine.DownPreviewEnabled = _original.NextDownPreview;
        GameEngine.NewSeriesLiveEnabled = _original.NewSeriesLive;
        DisplayTiming.Apply(_original);
        return true;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Commit()) return;
        _committed = true;
        Close();
    }

    /// <summary>
    /// Without an Enter save, Cancel reports false and MainWindow leaves the live theme untouched.
    /// After one, the values are already stored, so closing reports true and the owner repaints.
    /// </summary>
    void Done_Click(object sender, RoutedEventArgs e)
    {
        if (_committed && false)
        {
            DialogResult = true; // this closes the dialog itself
            return;
        }
        Close();
    }
}
