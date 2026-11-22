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
    public bool Highlighter { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>What the pointer draws with while the annotation layer is open.</summary>
public enum AnnotationTool { Pen, Highlighter, Eraser }

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

    /// <summary>When the newest point arrived, so every output fades the same ink together.</summary>
    public DateTimeOffset LastInkAt { get; private set; } = DateTimeOffset.MinValue;

    public event Action? HoldChanged;
    public event Action? Dismissed;
    public event Action? StrokeStarted;
    public event Action? StrokeFinished;
    /// <summary>Any change to the shared ink, so every window repaints it.</summary>
    public event Action? Changed;

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

    /// <summary>Marks the newest ink point; the fade clock is shared by every output.</summary>
    public void Touch()
    {
        LastInkAt = DateTimeOffset.UtcNow;
        Changed?.Invoke();
    }

    /// <summary>Starts a live stroke on every window, and holds the fade until it is finished.</summary>
    public void BeginStroke()
    {
        if (IsDrawing) return;
        IsDrawing = true;
        LastInkAt = DateTimeOffset.UtcNow;
        StrokeStarted?.Invoke();
        Changed?.Invoke();
    }

    public void EndStroke()
    {
        if (!IsDrawing) return;
        IsDrawing = false;
        LastInkAt = DateTimeOffset.UtcNow;
        StrokeFinished?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Drops strokes past their hold. A zero hold keeps ink until Clear or Dismiss.</summary>
    public bool Trim()
    {
        if (Hold <= TimeSpan.Zero || IsDrawing) return false;
        var cutoff = DateTimeOffset.UtcNow - Hold;
        var before = Strokes.Count;
        Strokes.RemoveAll(stroke => stroke.CreatedAt < cutoff);
        if (Strokes.Count == before) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes one stroke, used by the eraser and by Undo.</summary>
    public void RemoveStroke(AnnotationStroke stroke)
    {
        if (!Strokes.Remove(stroke)) return;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Strokes.Count == 0) return;
        Strokes.Clear();
        LastInkAt = DateTimeOffset.MinValue;
        Changed?.Invoke();
    }

    public void Dismiss()
    {
        if (IsDrawing) EndStroke();
        if (!IsOpen && Strokes.Count == 0) return;
        IsOpen = false;
        Strokes.Clear();
        LastInkAt = DateTimeOffset.MinValue;
        Changed?.Invoke();
        Dismissed?.Invoke();
    }
}

/// <summary>
/// Draws the shared strokes over the audience graphic and turns the pointer into new ink. It sits
/// above the scorebug, so a stroke never edits or moves the layout underneath. The surface is the
/// layout's design size, so the same stroke lands on the same artwork on every output.
/// </summary>
public sealed class TelestratorOverlay : Control
{
    const double FadeMilliseconds = 180;
    readonly AnnotationBoard _board;
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    AnnotationStroke? _active;
    IPointer? _pointer;
    DateTimeOffset _fadeStarted;
    bool _fading;

    public TelestratorOverlay(AnnotationBoard board)
    {
        _board = board;
        Focusable = true;
        ClipToBounds = true;
        _board.Changed += OnBoardChanged;
        _board.Dismissed += OnDismissed;
        _clock.Tick += (_, _) => Tick();
        _clock.Start();
    }

    public AnnotationTool Tool { get; private set; } = AnnotationTool.Pen;
    public Color InkColor { get; private set; } = Color.FromRgb(0xFF, 0xD1, 0x66);
    public double InkWidth { get; private set; } = 3;
    public bool Highlighter => Tool == AnnotationTool.Highlighter;
    public bool IsFading => _fading;

    public void SetTool(AnnotationTool tool)
    {
        if (_active is not null) FinishStroke();
        Tool = tool;
        Cursor = new Cursor(tool == AnnotationTool.Eraser ? StandardCursorType.Hand : StandardCursorType.Cross);
    }

    public void SetInk(Color color, double width)
    {
        InkColor = color;
        InkWidth = Math.Clamp(width, 1, 24);
        InvalidateVisual();
    }

    /// <summary>Clears the ink on every output, not just this one.</summary>
    public void ClearInk()
    {
        _fading = false;
        Opacity = 1;
        _board.Clear();
    }

    /// <summary>Drops the newest stroke from every output.</summary>
    public void UndoInk()
    {
        if (_board.Strokes.Count == 0) return;
        _board.RemoveStroke(_board.Strokes[^1]);
    }

    public void BeginFade()
    {
        if (_fading || _board.Strokes.Count == 0) return;
        _fading = true;
        _fadeStarted = DateTimeOffset.UtcNow;
        _clock.Start();
    }

    void Tick()
    {
        if (_fading)
        {
            var progress = Math.Clamp((DateTimeOffset.UtcNow - _fadeStarted).TotalMilliseconds / FadeMilliseconds, 0, 1);
            Opacity = 1 - progress;
            if (progress >= 1)
            {
                _fading = false;
                Opacity = 1;
                _board.Clear();
            }
            InvalidateVisual();
            return;
        }
        if (_board.Strokes.Count == 0 || _board.IsDrawing || _board.Hold <= TimeSpan.Zero) return;
        if (DateTimeOffset.UtcNow - _board.LastInkAt < _board.Hold) return;
        BeginFade();
    }

    void OnBoardChanged() => InvalidateVisual();

    void OnDismissed()
    {
        _fading = false;
        Opacity = 1;
        InvalidateVisual();
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        foreach (var stroke in _board.Strokes)
        {
            if (stroke.Points.Count < 2) continue;
            var color = stroke.Highlighter
                ? Color.FromArgb(0x66, stroke.Color.R, stroke.Color.G, stroke.Color.B)
                : stroke.Color;
            var pen = new Pen(new SolidColorBrush(color), Math.Max(0.5, stroke.Width));
            for (var i = 1; i < stroke.Points.Count; i++)
                context.DrawLine(pen, stroke.Points[i - 1], stroke.Points[i]);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!_board.IsOpen || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        if (Tool == AnnotationTool.Eraser)
        {
            EraseAt(point);
            e.Handled = true;
            return;
        }
        _pointer = e.Pointer;
        _pointer.Capture(this);
        _active = new AnnotationStroke
        {
            Color = InkColor,
            Width = Tool == AnnotationTool.Highlighter ? Math.Max(12, InkWidth * 5) : InkWidth,
            Highlighter = Tool == AnnotationTool.Highlighter
        };
        _active.Points.Add(point);
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
        _board.Touch();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_active is null) return;
        _active.Points.Add(e.GetPosition(this));
        FinishStroke();
        e.Handled = true;
    }

    void FinishStroke()
    {
        _active = null;
        _board.EndStroke();
        _pointer?.Capture(null);
        _pointer = null;
        InvalidateVisual();
    }

    /// <summary>The eraser takes a whole stroke, the same unit Undo removes.</summary>
    void EraseAt(Point point)
    {
        var radius = Math.Max(8, InkWidth * 2);
        AnnotationStroke? nearest = null;
        var nearestDistance = double.MaxValue;
        foreach (var stroke in _board.Strokes)
        {
            for (var i = 1; i < stroke.Points.Count; i++)
            {
                var distance = DistanceToSegment(point, stroke.Points[i - 1], stroke.Points[i]);
                if (distance < nearestDistance) { nearestDistance = distance; nearest = stroke; }
            }
            if (stroke.Points.Count == 1)
            {
                var distance = Distance(point, stroke.Points[0]);
                if (distance < nearestDistance) { nearestDistance = distance; nearest = stroke; }
            }
        }
        if (nearest is not null && nearestDistance <= radius)
        {
            _board.RemoveStroke(nearest);
            InvalidateVisual();
        }
    }

    static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    static double DistanceToSegment(Point point, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = dx * dx + dy * dy;
        if (length < 0.0001) return Distance(point, a);
        var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / length, 0, 1);
        return Distance(point, new Point(a.X + t * dx, a.Y + t * dy));
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _clock.Stop();
        _board.Changed -= OnBoardChanged;
        _board.Dismissed -= OnDismissed;
        base.OnDetachedFromVisualTree(e);
    }
}
