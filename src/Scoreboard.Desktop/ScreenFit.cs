using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// Keeps operator windows usable on any monitor, including a portrait screen or a laptop panel at
/// 150% scaling. A window is never allowed to stay wider or taller than the monitor it sits on, so
/// dragging it onto a vertical display shrinks it into that display's work area instead of leaving
/// half the console off-screen.
/// </summary>
internal static class ScreenFit
{
    const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>Follows the window from monitor to monitor and keeps its bounds on the current one.</summary>
    public static void Track(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
        window.LocationChanged += (_, _) => Apply(window);
    }

    /// <summary>Clamps the window to the work area of the monitor it is on, in layout units.</summary>
    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var dpi = VisualTreeHelper.GetDpi(window);
        // Leave a little room for the window frame, which Windows measures outside the client area.
        var width = Math.Max(320, info.Work.Width / dpi.DpiScaleX - 8);
        var height = Math.Max(240, info.Work.Height / dpi.DpiScaleY - 8);

        window.MaxWidth = width;
        window.MaxHeight = height;
        if (window.Width > width) window.Width = width;
        if (window.Height > height) window.Height = height;
    }
}
