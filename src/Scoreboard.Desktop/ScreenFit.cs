using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Scoreboard.Desktop;

/// <summary>
/// Keeps operator windows usable on any monitor, including a portrait screen or a laptop panel at
/// 150% scaling. A window is never allowed to stay wider or taller than the monitor it sits on, so
/// dragging it onto a vertical display shrinks it into that display's work area instead of leaving
/// half the console off-screen.
/// </summary>
internal static class ScreenFit
{
    /// <summary>How much of the work area a fixed-size dialog keeps free around itself.</summary>
    public const double DialogMargin = 32;

    /// <summary>The smallest logical area a window is still allowed to occupy.</summary>
    const double FloorWidth = 320;
    const double FloorHeight = 240;

    /// <summary>The bounds a window needs, in logical units, after being fitted to a work area.</summary>
    internal readonly record struct FitResult(
        double MinWidth, double MinHeight,
        double Width, double Height,
        double MaxWidth, double MaxHeight);

    /// <summary>
    /// Fits a window to a work area. This is the arithmetic behind <see cref="Apply"/> and
    /// <see cref="FitDialog"/>, kept pure so the small-screen cases can be checked directly.
    /// <para>
    /// The design minimum is clamped <em>down</em> to the work area, never up: a 900 px minimum on
    /// an 853 px screen becomes 853, which is what makes a window smaller than its design size
    /// possible at all. A NaN or zero desired size means "let the window size itself"; it is left
    /// alone and the maximum bounds keep the result on screen.
    /// </para>
    /// </summary>
    public static FitResult ComputeFit(Size workArea, Size designMinimum, Size desired)
    {
        // A broken screen report must not make every window unusable.
        var workWidth = double.IsFinite(workArea.Width) && workArea.Width > 0
            ? Math.Max(FloorWidth, workArea.Width)
            : FloorWidth;
        var workHeight = double.IsFinite(workArea.Height) && workArea.Height > 0
            ? Math.Max(FloorHeight, workArea.Height)
            : FloorHeight;

        var minWidth = designMinimum.Width > 0 && double.IsFinite(designMinimum.Width)
            ? Math.Min(designMinimum.Width, workWidth)
            : 0;
        var minHeight = designMinimum.Height > 0 && double.IsFinite(designMinimum.Height)
            ? Math.Min(designMinimum.Height, workHeight)
            : 0;

        return new FitResult(
            minWidth, minHeight,
            Cap(desired.Width, workWidth, minWidth), Cap(desired.Height, workHeight, minHeight),
            workWidth, workHeight);
    }

    /// <summary>
    /// A requested size capped to the work area, but never raised above the requested size: an
    /// auto-sized window keeps its NaN and is capped by <see cref="FitResult.MaxWidth"/> instead.
    /// </summary>
    static double Cap(double desired, double limit, double minimum)
    {
        if (!double.IsFinite(desired) || desired <= 0) return desired;
        return Math.Min(Math.Max(desired, minimum), limit);
    }

    /// <summary>Follows the window from monitor to monitor and keeps its bounds on the current one.</summary>
    public static void Track(Avalonia.Controls.Window window) => Watch(window, margin: 0);

    /// <summary>
    /// Fits a fixed-size dialog, leaving <see cref="DialogMargin"/> around it. A dialog designed at
    /// 560×900 cannot fit a 768 px screen, so the same routine caps it and lets its content scroll.
    /// </summary>
    public static void FitDialog(Avalonia.Controls.Window window) => Watch(window, DialogMargin);

    static void Watch(Avalonia.Controls.Window window, double margin)
    {
        // WPF's SourceInitialized is Avalonia's Opened, LocationChanged is PositionChanged, and the
        // maximized state arrives as a WindowState property change rather than its own event.
        window.Opened += (_, _) => Apply(window, margin);
        window.PositionChanged += (_, _) => Apply(window, margin);
        window.Resized += (_, _) => Apply(window, margin);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Window.WindowStateProperty) Apply(window, margin);
        };
        // Fit once now, not only when the window opens: the platform hands the window manager the
        // minimum size as the window is mapped, and an oversized design minimum (the studio's
        // 1120 px, for example) would otherwise make the first frame too large for a small screen.
        // A window with no screen to measure against yet is fitted when it opens.
        Apply(window, margin);
    }

    /// <summary>
    /// Keeps the window inside the monitor it is on, in layout units. A maximized window is measured
    /// against the whole monitor rather than the work area: clamping it to the work area minus a
    /// frame margin left a strip of desktop down the side and bottom, which is exactly the window
    /// that would not go full screen on any monitor, portrait included.
    /// <para>
    /// Monitor geometry comes from Avalonia's screen backend — xrandr under X11, the portal under
    /// Wayland — divided by that monitor's scale factor, which is the same division the Windows port
    /// did with GetDpi. No win32 handles, and Wayland's no-coordinate model cannot be measured any
    /// other way.
    /// </para>
    /// </summary>
    public static void Apply(Avalonia.Controls.Window window) => Apply(window, margin: 0);

    static void Apply(Avalonia.Controls.Window window, double margin)
    {
        Avalonia.Platform.Screen? screen;
        bool maximized;
        Size workArea;
        try
        {
            var screens = window.Screens;
            screen = screens?.ScreenFromWindow(window) ?? screens?.Primary;
            if (screen is null) return;
            maximized = window.WindowState == WindowState.Maximized;
            var area = maximized ? screen.Bounds : screen.WorkingArea;
            var scaling = screen.Scaling > 0 ? screen.Scaling : window.RenderScaling;
            if (scaling <= 0) scaling = 1;
            // A maximized window already owns the whole monitor, so no desktop margin applies.
            var inset = maximized ? 0 : margin;
            workArea = new Size(
                Math.Max(FloorWidth, area.Width / scaling - inset),
                Math.Max(FloorHeight, area.Height / scaling - inset));
        }
        // A window that has never been shown has no platform screen to measure against yet.
        catch (Exception) { return; }

        ApplyTo(window, workArea, maximized);
    }

    /// <summary>Applies a known work area. Used by the fit checks to simulate a small screen.</summary>
    internal static void ApplyTo(Avalonia.Controls.Window window, Size workArea, bool maximized)
    {
        var fit = ComputeFit(workArea,
            new Size(window.MinWidth, window.MinHeight),
            new Size(window.Width, window.Height));

        if (window.MaxWidth != fit.MaxWidth) window.MaxWidth = fit.MaxWidth;
        if (window.MaxHeight != fit.MaxHeight) window.MaxHeight = fit.MaxHeight;
        if (window.MinWidth != fit.MinWidth) window.MinWidth = fit.MinWidth;
        if (window.MinHeight != fit.MinHeight) window.MinHeight = fit.MinHeight;

        // Only a floating window is pulled back in; resizing a maximized one would knock it out of
        // the maximized state the desktop just applied. Auto-sized windows keep their NaN and are
        // bounded by MaxWidth/MaxHeight instead.
        if (maximized) return;
        if (double.IsFinite(fit.Width) && fit.Width > 0 && window.Width != fit.Width) window.Width = fit.Width;
        if (double.IsFinite(fit.Height) && fit.Height > 0 && window.Height != fit.Height) window.Height = fit.Height;
    }
}
