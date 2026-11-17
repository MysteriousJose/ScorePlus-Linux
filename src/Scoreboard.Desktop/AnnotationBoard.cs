using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Scoreboard.Desktop;

/// <summary>One pen or highlighter stroke, in surface coordinates.</summary>
public sealed class AnnotationStroke
{
    public List<Point> Points { get; } = [];
    public Color Color { get; init; } = Colors.White;
    public double Width { get; init; } = 3;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// The ink shared by every audience window for one display. The operator draws on one window and
/// the same strokes appear on the others, so the projector and the operator's copy cannot disagree.
/// The collection belongs to the show, not to a window; closing one output never discards the ink
/// another output is showing.
/// </summary>
public sealed class AnnotationBoard
{
    static readonly Dictionary<string, AnnotationBoard> Registry = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The board for a display, created once and shared by every output showing it.</summary>
    public static AnnotationBoard ForDisplay(string? displayId, TimeSpan? hold = null)
    {
        var key = string.IsNullOrWhiteSpace(displayId) ? "program" : displayId.Trim();
        if (!Registry.TryGetValue(key, out var board))
        {
            board = new AnnotationBoard(hold ?? TimeSpan.FromSeconds(1));
            Registry[key] = board;
        }
        else if (hold is { } wanted) board.SetHold(wanted);
        return board;
    }

    public List<AnnotationStroke> Strokes { get; } = [];

    /// <summary>Zero means permanent; otherwise at least half a second.</summary>
    public TimeSpan Hold { get; private set; } = TimeSpan.FromSeconds(1);

    /// <summary>True while a draw session is up on any window for this display.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True between the first point and the pointer release of a live stroke.</summary>
    public bool IsDrawing { get; private set; }

    public event Action? HoldChanged;
    public event Action? Dismissed;
    public event Action? StrokeStarted;
    public event Action? StrokeFinished;

    public AnnotationBoard(TimeSpan? hold = null) => Hold = Normalize(hold ?? TimeSpan.FromSeconds(1));

    public static TimeSpan Normalize(TimeSpan hold) => hold <= TimeSpan.Zero
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Math.Clamp(hold.TotalSeconds, 0.5, 10));

    public void SetHold(TimeSpan hold)
    {
        var normalized = Normalize(hold);
        if (normalized == Hold) return;
        Hold = normalized;
        HoldChanged?.Invoke();
    }

    public void Open() => IsOpen = true;

    /// <summary>Starts a live stroke on every window, and holds the fade until it is finished.</summary>
    public void BeginStroke()
    {
        if (IsDrawing) return;
        IsDrawing = true;
        StrokeStarted?.Invoke();
    }

    public void EndStroke()
    {
        if (!IsDrawing) return;
        IsDrawing = false;
        StrokeFinished?.Invoke();
    }

    /// <summary>Drops strokes past their hold. A zero hold keeps ink until Clear or Dismiss.</summary>
    public bool Trim()
    {
        if (Hold <= TimeSpan.Zero || IsDrawing) return false;
        var cutoff = DateTimeOffset.UtcNow - Hold;
        var before = Strokes.Count;
        Strokes.RemoveAll(stroke => stroke.CreatedAt < cutoff);
        return Strokes.Count != before;
    }

    public void Clear() => Strokes.Clear();

    public void Dismiss()
    {
        if (IsDrawing) EndStroke();
        if (!IsOpen && Strokes.Count == 0) return;
        IsOpen = false;
        Strokes.Clear();
        Dismissed?.Invoke();
    }
}

/// <summary>
/// Draws the shared strokes over the audience graphic and turns the pointer into new ink. It sits
/// above the scorebug, so a stroke never edits or moves the layout underneath.
/// </summary>
public sealed class TelestratorOverlay : Control
{
    readonly AnnotationBoard _board;
    readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromMilliseconds(200) };
    AnnotationStroke? _active;
    IPointer? _pointer;

    public TelestratorOverlay(AnnotationBoard board)
    {
        _board = board;
        Focusable = true;
        ClipToBounds = true;
        _fade.Tick += (_, _) =>
        {
            if (_board.Trim()) InvalidateVisual();
        };
        _fade.Start();
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        foreach (var stroke in _board.Strokes)
        {
            if (stroke.Points.Count < 2) continue;
            var pen = new Pen(new SolidColorBrush(stroke.Color), Math.Max(0.5, stroke.Width));
            for (var i = 1; i < stroke.Points.Count; i++)
                context.DrawLine(pen, stroke.Points[i - 1], stroke.Points[i]);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!_board.IsOpen || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pointer = e.Pointer;
        _pointer.Capture(this);
        _active = new AnnotationStroke
        {
            Color = Color.FromRgb(0xFF, 0xD1, 0x66),
            Width = Math.Clamp(PreferencesStore.Load().AnnotationWidth, 1, 24)
        };
        _active.Points.Add(e.GetPosition(this));
        _board.Strokes.Add(_active);
        _board.BeginStroke();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_active is null || _pointer is null) return;
        var point = e.GetPosition(this);
        if (_active.Points.Count > 0)
        {
            var last = _active.Points[^1];
            if (Math.Abs(point.X - last.X) + Math.Abs(point.Y - last.Y) < 1.5) return;
        }
        _active.Points.Add(point);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_active is null) return;
        _active.Points.Add(e.GetPosition(this));
        _active = null;
        _board.EndStroke();
        _pointer?.Capture(null);
        _pointer = null;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _fade.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
