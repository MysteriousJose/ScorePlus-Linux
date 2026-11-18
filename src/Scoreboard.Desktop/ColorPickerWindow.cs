using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Scoreboard.Desktop;

/// <summary>Custom color picker: type a hex value, drag RGB/alpha sliders, tap a palette swatch, or sample any pixel on screen.</summary>
public sealed class ColorPickerWindow : Window
{
    readonly TextBox _hex = new();
    readonly Border _preview = new();
    readonly Slider _red = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly Slider _green = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly Slider _blue = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly Slider _alpha = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, Value = 255 };
    readonly TextBlock _redText = new(), _greenText = new(), _blueText = new(), _alphaText = new();
    bool _updating;

    public string SelectedHex { get; private set; } = "#FFFFFFFF";

    public ColorPickerWindow(string initial)
    {
        Title = "Choose color";
        Width = 480; SizeToContent = SizeToContent.Height; MinWidth = 440;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "CUSTOM COLOR", FontWeight = FontWeights.Bold, Margin = new Thickness(2, 0, 0, 8) });

        var previewRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _preview.Height = 48; _preview.CornerRadius = new CornerRadius(6);
        _preview.BorderBrush = Brushes.Gray; _preview.BorderThickness = new Thickness(1);
        DockPanel.SetDock(_preview, Dock.Left);
        _preview.Width = 120;
        // This preview must show the chosen color, so theming has to leave it alone.
        ThemeManager.SetSkipTheme(_preview, true);
        previewRow.Children.Add(_preview);
        var hexPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        hexPanel.Children.Add(new TextBlock { Text = "Hex (#RRGGBB or #AARRGGBB)", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), FontSize = 11 });
        hexPanel.Children.Add(_hex);
        previewRow.Children.Add(hexPanel);
        root.Children.Add(previewRow);

        root.Children.Add(SliderRow("Red", _red, _redText));
        root.Children.Add(SliderRow("Green", _green, _greenText));
        root.Children.Add(SliderRow("Blue", _blue, _blueText));
        root.Children.Add(SliderRow("Alpha", _alpha, _alphaText));

        // The eyedropper is deliberately beside the palette: every color menu in ScorePlus uses
        // this window, so adding it here makes it available to every color field at once.
        var screenButton = new Button
        {
            Content = "Pick from screen…",
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(2, 8, 0, 0),
            ToolTip = "Click anywhere on the screen and use that pixel's color"
        };
        screenButton.Click += (_, _) => PickFromScreen();
        root.Children.Add(screenButton);

        var palette = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var swatches = new[]
        {
            Colors.White, Colors.Black, Colors.Red, Colors.Orange, Colors.Gold, Colors.Yellow, Colors.LimeGreen, Colors.Green,
            Colors.Teal, Colors.Cyan, Colors.DodgerBlue, Colors.Navy, Colors.Purple, Colors.Magenta, Colors.SlateGray, Colors.DimGray
        };
        foreach (var color in swatches)
        {
            var swatch = new Button
            {
                Width = 30, Height = 26, Margin = new Thickness(2), Background = new SolidColorBrush(color),
                BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), ToolTip = color.ToString()
            };
            ThemeManager.SetSkipTheme(swatch, true);
            swatch.Click += (_, _) => SetHex($"#FF{color.R:X2}{color.G:X2}{color.B:X2}");
            palette.Children.Add(swatch);
        }
        root.Children.Add(palette);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var use = new Button { Content = "Use color", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        use.Click += (_, _) =>
        {
            if (!TryParse(_hex.Text, out _)) return;
            SelectedHex = Normalize(_hex.Text ?? "");
            CloseWith(true);
        };
        cancel.Click += (_, _) => CloseWith(false);
        buttons.Children.Add(use); buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;

        _hex.TextChanged += (_, _) => { if (_updating) return; if (TryParse(_hex.Text, out var color)) ApplyColor(color, syncHex: false); };
        _red.ValueChanged += (_, _) => SliderChanged();
        _green.ValueChanged += (_, _) => SliderChanged();
        _blue.ValueChanged += (_, _) => SliderChanged();
        _alpha.ValueChanged += (_, _) => SliderChanged();

        ThemeManager.Apply(this, PreferencesStore.Load().Theme);
        SetHex(initial);
        Loaded += (_, _) => { _hex.Focus(); _hex.SelectAll(); };
    }

    static FrameworkElement SliderRow(string label, Slider slider, TextBlock valueText)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var name = new TextBlock { Text = label, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)) };
        valueText.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right;
        valueText.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
        Grid.SetColumn(name, 0); Grid.SetColumn(slider, 1); Grid.SetColumn(valueText, 2);
        grid.Children.Add(name); grid.Children.Add(slider); grid.Children.Add(valueText);
        return grid;
    }

    void SliderChanged()
    {
        if (_updating) return;
        var color = Color.FromArgb((byte)_alpha.Value, (byte)_red.Value, (byte)_green.Value, (byte)_blue.Value);
        ApplyColor(color, syncHex: true);
    }

    void ApplyColor(Color color, bool syncHex)
    {
        _updating = true;
        _red.Value = color.R; _green.Value = color.G; _blue.Value = color.B; _alpha.Value = color.A;
        _redText.Text = color.R.ToString(); _greenText.Text = color.G.ToString(); _blueText.Text = color.B.ToString(); _alphaText.Text = color.A.ToString();
        _preview.Background = new SolidColorBrush(color);
        if (syncHex) _hex.Text = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        _updating = false;
    }

    void SetHex(string value)
    {
        if (TryParse(value, out var color)) ApplyColor(color, syncHex: false);
        else ApplyColor(Colors.White, syncHex: false);
        _updating = true;
        _hex.Text = value;
        _updating = false;
    }

    static bool TryParse(string? text, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString((text ?? "").Trim())!;
            return true;
        }
        catch { color = Colors.White; return false; }
    }

    static string Normalize(string text) => text.Trim().ToUpperInvariant();

    /// <summary>Hides the dialog behind a full-desktop overlay, then samples the pixel the operator clicks.</summary>
    void PickFromScreen()
    {
        var sampler = new ScreenColorPickerWindow();
        Opacity = 0.01;
        try
        {
            if (sampler.ShowDialog() == true && !string.IsNullOrWhiteSpace(sampler.PickedHex))
                SetHex(sampler.PickedHex);
        }
        finally
        {
            Opacity = 1;
            Activate();
        }
    }

    /// <summary>Closes a modal dialog; a modeless caller must not have DialogResult assigned.</summary>
    void CloseWith(bool result)
    {
        DialogResult = result;
    }

    /// <summary>Shows the picker and returns the chosen hex color, or null when cancelled.</summary>
    public static string? Pick(Window owner, string? initial)
    {
        var dialog = new ColorPickerWindow(initial ?? "") { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedHex : null;
    }

    /// <summary>
    /// Shows the picker beside its owner rather than over it, so the window whose colour is being
    /// chosen — team details, with its logo and palette — stays visible while the shade is picked.
    /// </summary>
    public static string? PickBeside(Window owner, string? initial)
    {
        var dialog = new ColorPickerWindow(initial ?? "")
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scaling = screen?.Scaling ?? 1.0;
        var dialogWidth = (int)Math.Round(dialog.Width * scaling);
        var ownerWidth = (int)Math.Round(owner.Bounds.Width);
        var left = owner.Position.X + ownerWidth + 10;
        // No room on the right: sit on the other side, still beside rather than on top.
        if (left + dialogWidth > area.Right) left = owner.Position.X - dialogWidth - 10;
        left = Math.Clamp(left, area.X + 8, Math.Max(area.X + 8, area.Right - dialogWidth - 8));
        // Height follows its content, so only the top edge can be placed; leave room for the body.
        var top = Math.Clamp(owner.Position.Y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - 540));
        dialog.Position = new PixelPoint(left, top);
        return dialog.ShowDialog() == true ? dialog.SelectedHex : null;
    }
}

/// <summary>A transparent full-screen overlay that turns one click into a desktop color sample.</summary>
internal sealed class ScreenColorPickerWindow : Window
{
    public string? PickedHex { get; private set; }

    public ScreenColorPickerWindow()
    {
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var screen = Screens.Primary;
        var bounds = screen?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
        var scaling = screen?.Scaling ?? 1.0;
        Position = bounds.Position;
        Width = Math.Max(1, bounds.Width / scaling);
        Height = Math.Max(1, bounds.Height / scaling);
        Cursor = new Cursor(StandardCursorType.Cross);
        // A tiny label makes it clear that the next click is the sample, not a UI action.
        var hint = new TextBlock
        {
            Text = "CLICK ANYWHERE TO PICK THAT COLOR",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(210, 9, 16, 24)),
            Padding = new Thickness(14, 8, 14, 8),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 26, 0, 0)
        };
        Content = hint;
        PointerPressed += async (_, e) =>
        {
            var point = this.PointToScreen(e.GetPosition(this));
            // Make the sampler transparent rather than hiding it: hiding a modal window can upset
            // the dialog result. A short delay lets the compositor paint the screen underneath.
            Opacity = 0;
            IsHitTestVisible = false;
            await Task.Delay(80);
            var color = ScreenPixel.Sample(point.X, point.Y);
            PickedHex = $"#FF{color.R:X2}{color.G:X2}{color.B:X2}";
            Opacity = 1;
            DialogResult = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            DialogResult = false;
        };
        Loaded += (_, _) => Activate();
    }
}

/// <summary>Reads one desktop pixel with ffmpeg's x11grab, so the eyedropper works without GDI.</summary>
internal static class ScreenPixel
{
    public static Color Sample(int x, int y)
    {
        if (MediaProbe.Ffmpeg is null) return Colors.White;
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrWhiteSpace(display)) display = ":0";
        try
        {
            using var process = Process.Start(MediaProbe.Start(MediaProbe.Ffmpeg,
                "-v", "error",
                "-f", "x11grab",
                "-video_size", "1x1",
                "-i", $"{display}+{Math.Max(0, x)},{Math.Max(0, y)}",
                "-frames:v", "1",
                "-f", "rawvideo",
                "-pix_fmt", "rgb24",
                "-"));
            if (process is null) return Colors.White;
            var frame = new byte[3];
            var read = process.StandardOutput.BaseStream.Read(frame, 0, frame.Length);
            process.WaitForExit(2000);
            return read < 3 ? Colors.White : Color.FromRgb(frame[0], frame[1], frame[2]);
        }
        catch { return Colors.White; }
    }
}
