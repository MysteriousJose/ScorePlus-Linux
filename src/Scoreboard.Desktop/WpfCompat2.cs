// ScorePlus Linux port — dirty WPF compatibility shims, layer 2 (members, helpers, dialogs).
// Everything here is inert glue so the ported code compiles on Avalonia; TODO(port) marks each
// site that needs a real implementation in a later phase.
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace Scoreboard.Desktop
{

// ---------- Keyboard / focus (WPF's System.Windows.Input.Keyboard) ----------

public static class Keyboard
{
    static Avalonia.Controls.Window? Main => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    public static object? FocusedElement => Main is { } w ? w.FocusManager?.GetFocusedElement() : null; // dirty: scoped to main window
    public static void ClearFocus() { if (Main is { } w) w.FocusManager?.ClearFocus(); }
}

// ---------- VisualTreeHelper (WPF static helper; Avalonia uses VisualTree extensions) ----------

public readonly struct DpiInformation
{
    public double DpiScaleX { get; }
    public double DpiScaleY { get; }
    public double PixelsPerDip => DpiScaleX;
    public DpiInformation(double x, double y) { DpiScaleX = x; DpiScaleY = y; }
}

public static class VisualTreeHelper
{
    public static int GetChildrenCount(Visual visual) => visual.GetVisualChildren().Count();
    public static Visual GetChild(Visual visual, int index) => visual.GetVisualChildren().ElementAt(index);
    public static Visual? GetParent(Visual visual) => visual.GetVisualParent();
    public static Avalonia.AvaloniaObject? GetParent(Avalonia.AvaloniaObject obj) =>
        obj is Visual v ? v.GetVisualParent() : null;
    // Dirty: ported walks pass AvaloniaObject roots (Theme/panels); unwrap controls, then visuals.
    public static int GetChildrenCount(Avalonia.AvaloniaObject root) => ChildrenOf(root).Count;
    public static Avalonia.AvaloniaObject GetChild(Avalonia.AvaloniaObject root, int index) => ChildrenOf(root)[index];
    static System.Collections.Generic.List<Avalonia.AvaloniaObject> ChildrenOf(Avalonia.AvaloniaObject root)
    {
        var list = new System.Collections.Generic.List<Avalonia.AvaloniaObject>();
        IEnumerable<object?> children = root switch
        {
            Panel panel => panel.Children.Cast<object?>(),
            ItemsControl items => items.Items.Cast<object?>(),
            ContentControl { Content: not null } content => [content.Content],
            Visual visual => visual.GetVisualChildren().Cast<object?>(),
            _ => [],
        };
        list.AddRange(children.OfType<Avalonia.AvaloniaObject>());
        return list;
    }
    // Dirty: WPF returns device DPI (96 baseline). Avalonia reports scaling; 96 keeps math stable.
    public static DpiInformation GetDpi(Visual visual) => new(96, 96);
}

// ---------- Cursors ----------

public static class Cursors
{
    public static readonly Cursor Hand = new(StandardCursorType.Hand);
    public static readonly Cursor IBeam = new(StandardCursorType.Ibeam);
    public static readonly Cursor SizeAll = new(StandardCursorType.SizeAll);
    public static readonly Cursor SizeWE = new(StandardCursorType.SizeWestEast);
    public static readonly Cursor SizeNWSE = new(StandardCursorType.BottomRightCorner);
    public static readonly Cursor Arrow = new(StandardCursorType.Arrow);
}

// ---------- ColorConverter ----------

public static class ColorConverter
{
    public static object ConvertFromString(string value) => Color.Parse(value);
}

// ---------- PointCollection / Int32Rect (WPF geometry helpers) ----------

public sealed class PointCollection : List<Point>
{
}

public readonly struct Int32Rect
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public Int32Rect(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
}

// ---------- Style / Setter (thin shim over Avalonia.Styling.Style) ----------

public sealed class Style
{
    public List<Setter> Setters { get; } = [];
    public string? Selector { get; set; }
}

public sealed class Setter
{
    public AvaloniaProperty? Property { get; set; }
    public object? Value { get; set; }
    public Setter() { }
    public Setter(AvaloniaProperty property, object value) { Property = property; Value = value; }
}

// ---------- WPF clipboard payload ----------

public interface IDataObject
{
    bool GetDataPresent(string format);
    object? GetData(string format);
}

internal sealed class ClipboardData : IDataObject
{
    readonly string? _text;
    public ClipboardData(string? text) { _text = text; }
    public bool GetDataPresent(string format) => format == "UnicodeText" || format == "Text";
    public object? GetData(string format) => GetDataPresent(format) ? _text : null;
}

// ---------- mouse/drag event-name adapters (used as extensions on +=/-= sites) ----------
// TECH DEBT: Avalonia has no *LeftButtonDown events; these extensions only compile if call
// sites use method syntax. Most sites get codemods to Pointer* instead; these cover stragglers.

public static class WpfMouseEvents
{
    public static void GotKeyboardFocus(this InputElement target, EventHandler<GotFocusEventArgs> handler)
        => target.AddHandler(InputElement.GotFocusEvent, handler, RoutingStrategies.Bubble);
    public static void LostKeyboardFocus(this InputElement target, EventHandler<RoutedEventArgs> handler)
        => target.AddHandler(InputElement.LostFocusEvent, handler, RoutingStrategies.Bubble);
    public static void DragEnter(this InputElement target, EventHandler<DragEventArgs> handler)
        => DragDrop.AddDragEnterHandler(target, handler);
    public static void DragOver(this InputElement target, EventHandler<DragEventArgs> handler)
        => DragDrop.AddDragOverHandler(target, handler);
    public static void DragLeave(this InputElement target, EventHandler<DragEventArgs> handler)
        => DragDrop.AddDragLeaveHandler(target, handler);
    public static void Drop(this InputElement target, EventHandler<DragEventArgs> handler)
        => DragDrop.AddDropHandler(target, handler);

    public static void MouseLeftButtonDown(this InputElement target, EventHandler<PointerPressedEventArgs> handler)
        => target.AddHandler(InputElement.PointerPressedEvent, handler, RoutingStrategies.Direct);
    public static void PreviewMouseLeftButtonDown(this InputElement target, EventHandler<PointerPressedEventArgs> handler)
        => target.AddHandler(InputElement.PointerPressedEvent, handler, RoutingStrategies.Tunnel);
    public static void MouseMove(this InputElement target, EventHandler<PointerEventArgs> handler)
        => target.AddHandler(InputElement.PointerMovedEvent, handler, RoutingStrategies.Direct);
    public static void PreviewMouseMove(this InputElement target, EventHandler<PointerEventArgs> handler)
        => target.AddHandler(InputElement.PointerMovedEvent, handler, RoutingStrategies.Tunnel);
    public static void MouseLeftButtonUp(this InputElement target, EventHandler<PointerReleasedEventArgs> handler)
        => target.AddHandler(InputElement.PointerReleasedEvent, handler, RoutingStrategies.Direct);
    public static void PreviewMouseLeftButtonUp(this InputElement target, EventHandler<PointerReleasedEventArgs> handler)
        => target.AddHandler(InputElement.PointerReleasedEvent, handler, RoutingStrategies.Tunnel);
    public static void MouseRightButtonUp(this InputElement target, EventHandler<PointerReleasedEventArgs> handler)
        => target.AddHandler(InputElement.PointerReleasedEvent, (sender, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right) handler(sender, e);
        }, RoutingStrategies.Direct);
    public static void LostMouseCapture(this InputElement target, EventHandler<PointerCaptureLostEventArgs> handler)
        => target.AddHandler(InputElement.PointerCaptureLostEvent, handler, RoutingStrategies.Direct);
    public static void RightTappedExt(this InputElement target, EventHandler<PointerPressedEventArgs> handler)
        => target.AddHandler(InputElement.PointerPressedEvent, handler, RoutingStrategies.Direct);
}

// ---------- modal dialogs: WPF-shaped ShowDialog over a nested dispatcher frame ----------

public static class WpfDialogExtensions
{
    /// <summary>
    /// WPF's blocking bool ShowDialog(): shows the window transient for its owner and runs a nested
    /// dispatcher frame until the window closes, then hands back the DialogResult the dialog set
    /// (null — the user closed the window or pressed Escape — reads as cancel, exactly as in WPF).
    /// <para>
    /// The frame matters. `Dispatcher.UIThread.PushFrame` is Avalonia's equivalent of the nested
    /// message pump WPF runs inside ShowDialog: input, layout, rendering AND timers keep running
    /// while the dialog is up. `RunJobs()` plus `Thread.Sleep` in a `while (!closed)` loop only
    /// drains work that is already queued, and Avalonia's timers are not queued work — they are
    /// scheduled by the platform loop — so every DispatcherTimer in the process stops firing for as
    /// long as a dialog is open. That was reproduced directly: the SCOREPLUS_WINDOW_DEBUG audit
    /// timers never ticked while the launch box was open, and ticked on time with the box skipped.
    /// </para>
    /// <para>
    /// The result channel is the compat shim's own `DialogResult`, not Avalonia's: this Avalonia
    /// version has no `CloseFinished` event, so the reflection that used to look for it found
    /// nothing and the old version of this method returned false no matter what the dialog did —
    /// which silently disabled all 49 `if (dialog.ShowDialog() == true)` call sites in the port.
    /// </para>
    /// <para>
    /// TODO(port): still modeless at the window-manager level. The dialog stays above its owner
    /// (transient-for) but the owner is not input-disabled the way WPF disables it; switching these
    /// call sites to Avalonia's async ShowDialog&lt;T&gt; is what gets true modality.
    /// </para>
    /// </summary>
    public static bool ShowDialogPumped(this Window window, Window? owner = null)
    {
        var frame = new Avalonia.Threading.DispatcherFrame(true);
        window.Closed += OnClosed;
        try
        {
            if (owner is not null) window.Show(owner);
            else window.Show();
            Avalonia.Threading.Dispatcher.UIThread.PushFrame(frame);
        }
        finally
        {
            window.Closed -= OnClosed;
        }
        // WPF call sites set `DialogResult = true/false` on the compat Window; its setter also
        // closes the window, so by the time the frame has exited the value is already final.
        return window.DialogResult ?? false;

        void OnClosed(object? sender, EventArgs e) => frame.Continue = false;
    }
}

/// <summary>
/// The Linux file chooser behind the WPF-shaped dialog shims. A desktop session gets zenity or
/// kdialog; a machine with no picker, no display, or SCOREPLUS_NATIVE_DIALOG=0 falls back to the
/// app's typed-path window, which always works and hands ffmpeg the real path it needs.
/// </summary>
public static class LinuxFileDialog
{
    public sealed record Request(
        string Title, string InitialPath, string Filter, bool Save, bool Folder, bool Multi,
        bool CheckFileExists, bool OverwritePrompt, string DefaultExt, bool AddExtension);

    enum PickOutcome { Unavailable, Cancelled, Picked }

    /// <summary>Returns the chosen paths, or null when the operator cancelled.</summary>
    public static IReadOnlyList<string>? Choose(Request request, Avalonia.Controls.Window? owner)
    {
        var initial = NormalizeInitial(request);
        var native = request with { InitialPath = initial };
        if (NativePickerWanted())
        {
            var zenity = TryZenity(native);
            if (zenity.Outcome == PickOutcome.Picked) return zenity.Paths;
            if (zenity.Outcome == PickOutcome.Cancelled) return null;
            var kdialog = TryKdialog(native);
            if (kdialog.Outcome == PickOutcome.Picked) return kdialog.Paths;
            if (kdialog.Outcome == PickOutcome.Cancelled) return null;
        }
        var typed = Scoreboard.Desktop.PathPromptWindow.Ask(owner,
            string.IsNullOrWhiteSpace(request.Title)
                ? request.Save ? "Save as" : request.Folder ? "Choose a folder" : "Open a file"
                : request.Title,
            initial, request.Save, request.Folder, request.CheckFileExists, request.OverwritePrompt);
        return typed is null ? null : [typed];
    }

    static bool NativePickerWanted()
    {
        var forced = Environment.GetEnvironmentVariable("SCOREPLUS_NATIVE_DIALOG");
        if (forced == "0") return false;
        if (forced == "1") return true;
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    }

    static string NormalizeInitial(Request request)
    {
        var initial = (request.InitialPath ?? "").Trim();
        if (initial.Length == 0) return initial;
        if (request.Save && !request.Folder) initial = ApplyDefaultExtension(initial, request.DefaultExt, request.AddExtension);
        try { return System.IO.Path.IsPathRooted(initial) ? initial : System.IO.Path.GetFullPath(initial); }
        catch { return initial; }
    }

    /// <summary>WPF appends DefaultExt to a save path with no extension; the Linux pickers do not.</summary>
    public static string ApplyDefaultExtension(string path, string extension, bool add)
    {
        if (!add || string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(extension)) return path;
        var trimmed = extension.Trim().TrimStart('.');
        if (trimmed.Length == 0 || System.IO.Path.HasExtension(path)) return path;
        return path + "." + trimmed;
    }

    /// <summary>Translates a WPF filter string into zenity's "Name | pattern pattern" rows.</summary>
    public static IReadOnlyList<string> Filters(string filter)
    {
        var result = new List<string>();
        foreach (var (name, patterns) in FilterPairs(filter))
            result.Add($"{(name.Length == 0 ? "All files" : name)} | {patterns}");
        return result;
    }

    static IReadOnlyList<(string Name, string Patterns)> FilterPairs(string filter)
    {
        var parts = (filter ?? "").Split('|');
        var pairs = new List<(string, string)>();
        for (var index = 0; index + 1 < parts.Length; index += 2)
        {
            var name = parts[index].Trim();
            var patterns = string.Join(" ", parts[index + 1]
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(pattern => pattern.Trim() == "*.*" ? "*" : pattern.Trim())
                .Where(pattern => pattern.Length > 0));
            if (patterns.Length > 0) pairs.Add((name, patterns));
        }
        return pairs;
    }

    static (PickOutcome Outcome, IReadOnlyList<string>? Paths) TryZenity(Request request)
    {
        try
        {
            var args = new List<string> { "--file-selection" };
            if (!string.IsNullOrWhiteSpace(request.Title)) { args.Add("--title"); args.Add(request.Title); }
            if (request.Folder) args.Add("--directory");
            else if (request.Save) args.Add("--save");
            if (request.Multi) { args.Add("--multiple"); args.Add("--separator=\n"); }
            if (!string.IsNullOrWhiteSpace(request.InitialPath)) { args.Add("--filename"); args.Add(request.InitialPath); }
            foreach (var filter in Filters(request.Filter)) { args.Add("--file-filter"); args.Add(filter); }

            using var process = Process.Start(MediaProbe.Start("zenity", args.ToArray()));
            if (process is null) return (PickOutcome.Unavailable, null);
            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit(120_000);
            if (process.ExitCode != 0) return (PickOutcome.Cancelled, null);
            var paths = Clean(output, request);
            return paths.Count == 0 ? (PickOutcome.Cancelled, null) : (PickOutcome.Picked, paths);
        }
        catch (System.ComponentModel.Win32Exception) { return (PickOutcome.Unavailable, null); }
        catch { return (PickOutcome.Cancelled, null); }
    }

    static (PickOutcome Outcome, IReadOnlyList<string>? Paths) TryKdialog(Request request)
    {
        try
        {
            var args = new List<string>
            {
                request.Folder ? "--getexistingdirectory" : request.Save ? "--getsavefilename" : "--getopenfilename",
                string.IsNullOrWhiteSpace(request.InitialPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : request.InitialPath
            };
            var filters = FilterPairs(request.Filter).Select(pair => $"{pair.Patterns}|{pair.Name}").ToArray();
            if (filters.Length > 0) args.Add(string.Join("\n", filters));
            if (request.Multi) { args.Add("--multiple"); args.Add("--separate-output"); }
            if (!string.IsNullOrWhiteSpace(request.Title)) { args.Add("--title"); args.Add(request.Title); }

            using var process = Process.Start(MediaProbe.Start("kdialog", args.ToArray()));
            if (process is null) return (PickOutcome.Unavailable, null);
            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit(120_000);
            if (process.ExitCode != 0) return (PickOutcome.Cancelled, null);
            var paths = Clean(output, request);
            return paths.Count == 0 ? (PickOutcome.Cancelled, null) : (PickOutcome.Picked, paths);
        }
        catch (System.ComponentModel.Win32Exception) { return (PickOutcome.Unavailable, null); }
        catch { return (PickOutcome.Cancelled, null); }
    }

    static IReadOnlyList<string> Clean(string output, Request request)
    {
        var paths = new List<string>();
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            try
            {
                var path = request.Save && !request.Folder
                    ? ApplyDefaultExtension(line, request.DefaultExt, request.AddExtension)
                    : line;
                paths.Add(System.IO.Path.GetFullPath(path));
            }
            catch { }
            if (!request.Multi) break;
        }
        return paths;
    }
}

} // namespace Scoreboard.Desktop

// ---------- Microsoft.Win32 file dialogs (real Linux pickers, WPF-shaped call surface) ----------

namespace Microsoft.Win32
{
    public enum FileDialogCharacterCasing { Default, Upper, Lower }

    public abstract class FileDialogStub
    {
        public string Title { get; set; } = "";
        public string Filter { get; set; } = "";
        public int FilterIndex { get; set; } = 1;
        public string InitialDirectory { get; set; } = "";
        public string DefaultExt { get; set; } = "";
        public bool AddExtension { get; set; } = true;
        public bool CheckFileExists { get; set; } = true;
        public bool CheckPathExists { get; set; } = true;
        public bool ValidateNames { get; set; } = true;
        public bool OverwritePrompt { get; set; } = true;
        public bool RestoreDirectory { get; set; }
        public FileDialogCharacterCasing CharacterCasing { get; set; }
        public string FileName { get; set; } = "";
        public string[] FileNames { get; set; } = [];

        protected abstract bool SaveMode { get; }
        protected virtual bool AllowsMultiple => false;

        public bool? ShowDialog() => Show(null);
        public bool? ShowDialog(object? owner) => Show(owner);

        bool? Show(object? owner)
        {
            var request = new Scoreboard.Desktop.LinuxFileDialog.Request(
                Title, InitialPath(), Filter, SaveMode, false, AllowsMultiple,
                CheckFileExists, OverwritePrompt, DefaultExt, AddExtension);
            var chosen = Scoreboard.Desktop.LinuxFileDialog.Choose(request, owner as Avalonia.Controls.Window);
            if (chosen is null || chosen.Count == 0) return null;
            FileNames = chosen.ToArray();
            FileName = FileNames[0];
            return true;
        }

        string InitialPath()
        {
            if (!string.IsNullOrWhiteSpace(FileName))
            {
                if (System.IO.Path.IsPathRooted(FileName)) return FileName;
                if (!string.IsNullOrWhiteSpace(InitialDirectory))
                    return System.IO.Path.Combine(InitialDirectory, FileName);
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return string.IsNullOrWhiteSpace(home) ? FileName : System.IO.Path.Combine(home, FileName);
            }
            return string.IsNullOrWhiteSpace(InitialDirectory) ? "" : InitialDirectory;
        }
    }

    public sealed class OpenFileDialog : FileDialogStub
    {
        public bool Multiselect { get; set; }
        public bool DereferenceLinks { get; set; } = true;
        protected override bool SaveMode => false;
        protected override bool AllowsMultiple => Multiselect;
    }

    public sealed class SaveFileDialog : FileDialogStub
    {
        public bool CreatePrompt { get; set; }
        protected override bool SaveMode => true;
    }

    public sealed class FolderBrowserDialog
    {
        public string Description { get; set; } = "";
        public string SelectedPath { get; set; } = "";
        public bool ShowNewFolderButton { get; set; }

        public bool? ShowDialog() => Show(null);
        public bool? ShowDialog(object? owner) => Show(owner);

        bool? Show(object? owner)
        {
            var request = new Scoreboard.Desktop.LinuxFileDialog.Request(
                Description, SelectedPath, "", false, true, false, false, false, "", false);
            var chosen = Scoreboard.Desktop.LinuxFileDialog.Choose(request, owner as Avalonia.Controls.Window);
            if (chosen is null || chosen.Count == 0) return null;
            SelectedPath = chosen[0];
            return true;
        }
    }
}
