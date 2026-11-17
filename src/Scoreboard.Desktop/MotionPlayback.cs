using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Plays a <see cref="MotionClip"/> on any element: a hand-over's exit and enter legs, a looping
/// ambient motion, or the flourish a new value plays. Every property the maker can key ends up on a
/// transform or an effect here, and colour keys repaint the text, plate and outline as they travel.
/// One player for the studio preview, the makers and the audience output, so what the operator
/// builds is exactly what goes to air.
/// </summary>
internal static class MotionPlayback
{
    static readonly ConditionalWeakTable<Control, StrongBox<double>> DesignRotations = new();
    static readonly ConditionalWeakTable<Control, List<DispatcherTimer>> BorderTimers = new();
    static readonly ConditionalWeakTable<AvaloniaObject, BrushSnapshot> ForegroundSnapshots = new();
    static readonly ConditionalWeakTable<AvaloniaObject, BrushSnapshot> BackgroundSnapshots = new();
    static readonly ConditionalWeakTable<AvaloniaObject, BrushSnapshot> BorderSnapshots = new();
    static readonly ConditionalWeakTable<SolidColorBrush, StrongBox<Color>> BrushColours = new();
    static readonly ConditionalWeakTable<Brush, StrongBox<double>> BrushOpacities = new();
    static readonly ConditionalWeakTable<Border, StrongBox<Thickness>> BorderThicknesses = new();

    /// <summary>The element's design rotation, composed with every clip played on its host.</summary>
    public static double GetBaseRotation(Control host) => DesignRotation(host);

    public static void SetBaseRotation(Control host, double angle) => SetDesignRotation(host, angle);

    /// <summary>Puts a host at its design angle when no motion is running.</summary>
    public static void SetDesignRotation(Control host, double angle)
    {
        DesignRotations.AddOrUpdate(host, new StrongBox<double>(double.IsFinite(angle) ? angle : 0));
        host.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        var design = DesignRotation(host);
        host.RenderTransform = Math.Abs(design) > 0.001 ? new RotateTransform(design) : null;
    }

    static double DesignRotation(Control host) => DesignRotations.TryGetValue(host, out var angle) ? angle.Value : 0;

    /// <summary>
    /// Runs one phase from its first keyframe to its last. The host's own opacity is treated as a
    /// ceiling, so a clip that fades to 0 hides a half-transparent element rather than brightening it.
    /// </summary>
    public static void PlayPhase(Control host, MotionPhase? phase, double baseOpacity, Action? done = null, double speed = 1, bool repeat = false, bool pingPong = false, string? pieceId = null)
        => PlayPhaseCore(host, phase, baseOpacity, done, speed, repeat, pingPong, pieceId, includeDescendants: true);

    /// <summary>
    /// Plays whole-graphic motion on the layout canvas without walking into the items. Colour and
    /// content keys still travel through <see cref="PlayPhase"/> on each item.
    /// </summary>
    internal static void PlayPhaseRootOnly(Control host, MotionPhase? phase, double baseOpacity, string? pieceId = null)
        => PlayPhaseCore(host, phase, baseOpacity, null, 1, false, false, pieceId, includeDescendants: false);

    static void PlayPhaseCore(Control host, MotionPhase? phase, double baseOpacity, Action? done, double speed, bool repeat, bool pingPong, string? pieceId, bool includeDescendants)
    {
        if (phase is null || !phase.Enabled || phase.Seconds <= 0.001 || phase.IsEmpty)
        {
            done?.Invoke();
            return;
        }
        // A whole-graphic play (pieceId null) only runs the tracks that belong to no piece, so a
        // scoped key never lands on the canvas instead of on the item it was authored for.
        var tracks = phase.Tracks.Where(track => Matches(track.PieceId, pieceId)).ToList();
        var colorTracks = phase.Colors.Where(track => Matches(track.PieceId, pieceId)).ToList();
        if (tracks.Count == 0 && colorTracks.Count == 0) { done?.Invoke(); return; }
        StopCore(host, includeDescendants);
        var seconds = Math.Max(0.05, phase.Seconds / Math.Clamp(speed <= 0 ? 1 : speed, 0.1, 8));
        var repeatBehaviour = repeat ? RepeatBehavior.Forever : new RepeatBehavior(1);

        var group = new TransformGroup();
        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform();
        var skew = new SkewTransform();
        var translate = new TranslateTransform();
        group.Children.Add(scale);
        group.Children.Add(rotate);
        group.Children.Add(skew);
        group.Children.Add(translate);
        // The design angle is part of the element, not part of the clip: keep it under every
        // transform the clip animates.
        group.Children.Add(new RotateTransform(DesignRotation(host)));
        host.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        host.RenderTransform = group;

        var finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            if (!repeat)
            {
                Reset(host, baseOpacity, includeDescendants);
                done?.Invoke();
            }
        }

        // Opacity always has a track so there is a reliable completion callback even for a clip that
        // only moves. A clip with no opacity keys holds the element at its normal level.
        var opacityTrack = tracks.FirstOrDefault(track => track.Target == MotionTarget.Opacity);
        var opacity = BuildDouble(opacityTrack, seconds, 0, 1, baseOpacity);
        opacity.RepeatBehavior = repeatBehaviour;
        opacity.AutoReverse = pingPong;
        opacity.Completed += (_, _) => Finish();
        host.BeginAnimation(Visual.OpacityProperty, opacity);

        foreach (var track in tracks)
        {
            if (track.Target == MotionTarget.Opacity) continue;
            switch (track.Target)
            {
                case MotionTarget.X:
                    AnimateTransform(translate, TranslateTransform.XProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Y:
                    AnimateTransform(translate, TranslateTransform.YProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Scale:
                    AnimateTransform(scale, ScaleTransform.ScaleXProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    AnimateTransform(scale, ScaleTransform.ScaleYProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.ScaleX:
                    AnimateTransform(scale, ScaleTransform.ScaleXProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.ScaleY:
                    AnimateTransform(scale, ScaleTransform.ScaleYProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Rotate:
                    AnimateTransform(rotate, RotateTransform.AngleProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.SkewX:
                    AnimateTransform(skew, SkewTransform.AngleXProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.SkewY:
                    AnimateTransform(skew, SkewTransform.AngleYProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Blur:
                    AnimateBlur(host, track, seconds, repeatBehaviour, pingPong);
                    break;
                case MotionTarget.Width:
                    AnimateElement(host, Layoutable.WidthProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Height:
                    AnimateElement(host, Layoutable.HeightProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.TextOpacity:
                    foreach (var text in ScopedDescendants(host, includeDescendants).OfType<TextBlock>())
                        AnimateElement(text, Visual.OpacityProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.BackgroundOpacity:
                    AnimateBrushOpacity(host, MotionColorRole.Background, track, seconds, repeatBehaviour, pingPong, includeDescendants);
                    break;
                case MotionTarget.BorderOpacity:
                    AnimateBrushOpacity(host, MotionColorRole.Border, track, seconds, repeatBehaviour, pingPong, includeDescendants);
                    break;
                case MotionTarget.FontSize:
                    foreach (var text in ScopedDescendants(host, includeDescendants).OfType<TextBlock>())
                        AnimateElement(text, TextBlock.FontSizeProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.BorderThickness:
                    AnimateBorderThickness(host, track, seconds, repeat, pingPong, includeDescendants);
                    break;
            }
        }

        // Colour keys travel with the phase, relative to the same clock.
        AnimateColours(host, colorTracks, seconds, repeatBehaviour, pingPong, includeDescendants);
    }

    /// <summary>
    /// True when a track belongs to the piece being played. An empty piece id is the whole graphic,
    /// so an element's play still receives the clip's global keys as well as its own.
    /// </summary>
    static bool Matches(string? trackPiece, string? pieceId)
    {
        if (string.IsNullOrWhiteSpace(trackPiece)) return true;
        return pieceId is not null && string.Equals(trackPiece, pieceId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Puts a phase at an instant, without animating: the scrub preview. Values are read straight
    /// from the tracks at <paramref name="time"/>, so dragging the ruler shows the frame it lands on.
    /// </summary>
    public static void ApplyAt(Control host, MotionPhase? phase, double time, string? pieceId = null)
    {
        if (phase is null) return;
        var tracks = phase.Tracks.Where(track => Matches(track.PieceId, pieceId)).ToList();
        var colorTracks = phase.Colors.Where(track => Matches(track.PieceId, pieceId)).ToList();
        Stop(host);
        host.Opacity = 1;
        host.Effect = null;
        var group = new TransformGroup();
        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform();
        var skew = new SkewTransform();
        var translate = new TranslateTransform();
        group.Children.Add(scale);
        group.Children.Add(rotate);
        group.Children.Add(skew);
        group.Children.Add(translate);
        group.Children.Add(new RotateTransform(DesignRotation(host)));
        host.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        host.RenderTransform = group;
        foreach (var track in tracks)
        {
            if (track.ValueAt(time) is not { } value) continue;
            switch (track.Target)
            {
                case MotionTarget.X: translate.X = value; break;
                case MotionTarget.Y: translate.Y = value; break;
                case MotionTarget.Scale: scale.ScaleX = value; scale.ScaleY = value; break;
                case MotionTarget.ScaleX: scale.ScaleX = value; break;
                case MotionTarget.ScaleY: scale.ScaleY = value; break;
                case MotionTarget.Rotate: rotate.Angle = value; break;
                case MotionTarget.SkewX: skew.AngleX = value; break;
                case MotionTarget.SkewY: skew.AngleY = value; break;
                case MotionTarget.Opacity: host.Opacity = Math.Clamp(value, 0, 1); break;
                case MotionTarget.Blur: host.Effect = new BlurEffect { Radius = Math.Max(0, value) }; break;
                case MotionTarget.Width: host.Width = Math.Max(0, value); break;
                case MotionTarget.Height: host.Height = Math.Max(0, value); break;
                case MotionTarget.TextOpacity:
                    foreach (var text in Descendants(host).OfType<TextBlock>()) text.Opacity = Math.Clamp(value, 0, 1);
                    break;
                case MotionTarget.BackgroundOpacity:
                    foreach (var slot in Slots(host, MotionColorRole.Background, includeDescendants: true)) slot.SetOpacity(Math.Clamp(value, 0, 1));
                    break;
                case MotionTarget.BorderOpacity:
                    foreach (var slot in Slots(host, MotionColorRole.Border, includeDescendants: true)) slot.SetOpacity(Math.Clamp(value, 0, 1));
                    break;
                case MotionTarget.FontSize:
                    foreach (var text in Descendants(host).OfType<TextBlock>()) text.FontSize = Math.Clamp(value, 1, 1000);
                    break;
                case MotionTarget.BorderThickness:
                    foreach (var border in Descendants(host).OfType<Border>()) border.BorderThickness = new Thickness(Math.Clamp(value, 0, 100));
                    foreach (var shape in Descendants(host).OfType<Shape>()) shape.StrokeThickness = Math.Clamp(value, 0, 100);
                    break;
            }
        }
        foreach (var track in colorTracks)
        {
            if (track.ColorAt(time) is not { } hex) continue;
            var color = Paint.ColorOf(hex, Colors.White);
            foreach (var slot in Slots(host, track.Role, includeDescendants: true)) slot.Set(color);
        }
    }

    /// <summary>Plays an animation clip's motion, either once or for as long as the graphic is up.</summary>
    public static void PlayClip(Control host, MotionClip? clip, double baseOpacity, bool ambient, Action? done = null, MotionPhase? phase = null)
    {
        if (clip is null || clip.IsEmpty) { done?.Invoke(); return; }
        phase ??= ambient
            ? clip.Phases.FirstOrDefault(candidate => candidate.Enabled && !candidate.IsEmpty && !candidate.IsTriggered)
              ?? clip.Phases.FirstOrDefault(candidate => candidate.Enabled && !candidate.IsEmpty)
            : clip.EnterPhase;
        if (phase is null) { done?.Invoke(); return; }
        PlayPhase(host, phase, baseOpacity, done, 1, repeat: ambient && clip.Loop, pingPong: ambient && clip.PingPong);
    }

    /// <summary>Stops every animation this player put on an element and leaves it at rest.</summary>
    public static void Stop(Control host) => StopCore(host, true);

    /// <summary>
    /// Stops the clip on one host. A whole-graphic score celebration passes false so its canvas
    /// transform can be cleared without disturbing the item animations running underneath.
    /// </summary>
    internal static void StopRootOnly(Control host) => StopCore(host, false);

    static void StopCore(Control host, bool includeDescendants)
    {
        StopBorderTimers(host, includeDescendants);
        host.BeginAnimation(Visual.OpacityProperty, null);
        host.BeginAnimation(Layoutable.WidthProperty, null);
        host.BeginAnimation(Layoutable.HeightProperty, null);
        switch (host.RenderTransform)
        {
            case ScaleTransform scale:
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                break;
            case RotateTransform rotate:
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                break;
            case TranslateTransform translate:
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                break;
            case SkewTransform skew:
                skew.BeginAnimation(SkewTransform.AngleXProperty, null);
                skew.BeginAnimation(SkewTransform.AngleYProperty, null);
                break;
            case TransformGroup group:
                foreach (var child in group.Children) StopTransform(child);
                break;
        }
        if (host.Effect is BlurEffect blur) blur.BeginAnimation(BlurEffect.RadiusProperty, null);
        // A clip never leaves a property behind: box size, text size, outline and every brush
        // return to the values the designer actually saved.
        foreach (var element in ScopedDescendants(host, includeDescendants))
        {
            if (element is TextBlock text)
            {
                text.BeginAnimation(Visual.OpacityProperty, null);
                text.BeginAnimation(TextBlock.FontSizeProperty, null);
            }
            else if (element is Border border)
            {
                border.BeginAnimation(Border.BorderThicknessProperty, null);
            }
            else if (element is Shape shape)
            {
                shape.BeginAnimation(Shape.StrokeThicknessProperty, null);
            }
        }
        RestoreBrushes(host, includeDescendants);
    }

    static void StopTransform(Transform transform)
    {
        switch (transform)
        {
            case ScaleTransform scale:
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                break;
            case RotateTransform rotate:
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                break;
            case TranslateTransform translate:
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                break;
            case SkewTransform skew:
                skew.BeginAnimation(SkewTransform.AngleXProperty, null);
                skew.BeginAnimation(SkewTransform.AngleYProperty, null);
                break;
        }
    }

    /// <summary>Puts an element back at rest at its design opacity.</summary>
    static void Reset(Control host, double baseOpacity, bool includeDescendants)
    {
        StopCore(host, includeDescendants);
        host.Opacity = Math.Clamp(baseOpacity, 0, 1);
        var angle = DesignRotation(host);
        host.RenderTransform = Math.Abs(angle) > 0.001 ? new RotateTransform(angle) : null;
        host.Effect = null;
    }

    static void AnimateTransform(Transform transform, AvaloniaProperty property, MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong, double fallback)
    {
        var animation = BuildDouble(track, seconds, 0, fallback, 1);
        animation.RepeatBehavior = repeat;
        animation.AutoReverse = pingPong;
        transform.BeginAnimation(property, animation);
    }

    static void AnimateBlur(Control host, MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong)
    {
        var blur = host.Effect as BlurEffect ?? new BlurEffect { Radius = 0 };
        host.Effect = blur;
        var animation = BuildDouble(track, seconds, 0, 0, 1);
        animation.RepeatBehavior = repeat;
        animation.AutoReverse = pingPong;
        blur.BeginAnimation(BlurEffect.RadiusProperty, animation);
    }

    static void AnimateElement(AvaloniaObject element, AvaloniaProperty property, MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong, double fallback)
    {
        var animation = BuildDouble(track, seconds, 0, fallback, 1);
        animation.RepeatBehavior = repeat;
        animation.AutoReverse = pingPong;
        element.BeginAnimation(property, animation);
    }

    static void AnimateBrushOpacity(Control host, MotionColorRole role, MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong, bool includeDescendants)
    {
        foreach (var slot in Slots(host, role, includeDescendants))
            slot.AnimateOpacity(track, seconds, repeat, pingPong);
    }

    static void AnimateBorderThickness(Control host, MotionTrack track, double seconds, bool repeat, bool pingPong, bool includeDescendants)
    {
        var keys = track.Keys.OrderBy(key => key.Time).ToList();
        if (keys.Count == 0) return;
        var borders = ScopedDescendants(host, includeDescendants).OfType<Border>().ToList();
        var shapes = ScopedDescendants(host, includeDescendants).OfType<Shape>().ToList();
        var repeats = repeat ? RepeatBehavior.Forever : new RepeatBehavior(1);
        foreach (var border in borders)
            if (!BorderThicknesses.TryGetValue(border, out _))
                BorderThicknesses.Add(border, new StrongBox<Thickness>(border.BorderThickness));
        foreach (var shape in shapes)
            AnimateElement(shape, Shape.StrokeThicknessProperty, track, seconds, repeats, pingPong, 0);

        if (borders.Count == 0) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var started = DateTime.UtcNow;
        void Tick(object? sender, EventArgs e)
        {
            var elapsed = Math.Max(0, (DateTime.UtcNow - started).TotalSeconds);
            var total = seconds * (pingPong ? 2 : 1);
            double at;
            if (repeat)
            {
                var cycle = elapsed % total;
                at = pingPong && cycle > seconds ? 2 * seconds - cycle : cycle;
            }
            else if (elapsed >= total)
            {
                at = pingPong ? 0 : seconds;
                timer.Stop();
            }
            else
            {
                at = pingPong && elapsed > seconds ? 2 * seconds - elapsed : elapsed;
            }
            if (track.ValueAt(Math.Clamp(at, 0, seconds)) is not { } value) return;
            foreach (var border in borders) border.BorderThickness = new Thickness(Math.Clamp(value, 0, 100));
        }
        timer.Tick += Tick;
        BorderTimers.GetOrCreateValue(host).Add(timer);
        timer.Start();
    }

    static void StopBorderTimers(Control host, bool includeDescendants)
    {
        if (BorderTimers.TryGetValue(host, out var timers))
        {
            foreach (var timer in timers) timer.Stop();
            BorderTimers.Remove(host);
        }
        foreach (var border in ScopedDescendants(host, includeDescendants).OfType<Border>())
        {
            if (!BorderThicknesses.TryGetValue(border, out var original)) continue;
            border.BorderThickness = original.Value;
            BorderThicknesses.Remove(border);
        }
    }

    /// <summary>
    /// Turns one track into keyframes. A key's easing shapes the approach into it, which is why the
    /// curve belongs to the key rather than to the whole clip. Opacity is multiplied by the host's
    /// own level so the element never becomes brighter than it was designed to be.
    /// </summary>
    static DoubleAnimationUsingKeyFrames BuildDouble(MotionTrack? track, double seconds, double defaultValue, double fallback, double ceiling)
    {
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd, Duration = TimeSpan.FromSeconds(seconds) };
        var keys = track?.Keys.OrderBy(key => key.Time).ToList() ?? [];
        if (keys.Count == 0)
        {
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(fallback, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(fallback, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
            return animation;
        }
        double Limit(double value) => Math.Clamp(value, -100000, 100000);
        if (keys[0].Time > 0.0001)
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(Limit(keys[0].Value), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        foreach (var key in keys)
        {
            var fraction = seconds <= 0.0001 ? 1 : Math.Clamp(key.Time / seconds, 0, 1);
            var value = Limit(key.Value);
            if (ceiling != 1) value = Math.Clamp(value, 0, 1) * ceiling;
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(fraction * seconds)))
            {
                EasingFunction = Ease(key.Ease)
            });
        }
        return animation;
    }

    static IEasingFunction Ease(MotionEase ease) => ease switch
    {
        MotionEase.Linear => new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        MotionEase.In => new CubicEase { EasingMode = EasingMode.EaseIn },
        MotionEase.Out => new CubicEase { EasingMode = EasingMode.EaseOut },
        MotionEase.InOut => new CubicEase { EasingMode = EasingMode.EaseInOut },
        MotionEase.Back => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
        MotionEase.Bounce => new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2, Bounciness = 2 },
        MotionEase.Elastic => new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 6 },
        _ => new QuadraticEase { EasingMode = EasingMode.EaseInOut }
    };

    // ---------- colour keys ----------

    /// <summary>
    /// Repaints the text, plate and outline through a phase's colour keys. Every brush is replaced
    /// with an unfrozen copy first, because an animated frozen brush throws.
    /// </summary>
    public static void AnimateColours(Control host, IEnumerable<ColorTrack> tracks, double seconds, RepeatBehavior repeat, bool pingPong, bool includeDescendants = true)
    {
        foreach (var track in tracks)
        {
            if (track.Keys.Count == 0) continue;
            var animation = BuildColour(track, seconds);
            animation.RepeatBehavior = repeat;
            animation.AutoReverse = pingPong;
            foreach (var slot in Slots(host, track.Role, includeDescendants)) slot.Animate(animation);
        }
    }

    /// <summary>Restores every brush a clip has touched, including colours held by an animation.</summary>
    static void RestoreBrushes(Control host, bool includeDescendants)
    {
        foreach (var role in new[] { MotionColorRole.Text, MotionColorRole.Background, MotionColorRole.Border })
            foreach (var slot in Slots(host, role, includeDescendants))
                slot.Restore();
    }

    static ColorAnimationUsingKeyFrames BuildColour(ColorTrack track, double seconds)
    {
        var animation = new ColorAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd, Duration = TimeSpan.FromSeconds(seconds) };
        var keys = track.Keys.OrderBy(key => key.Time).ToList();
        if (keys.Count == 0) return animation;
        Color Of(string? value) => Paint.ColorOf(value, Colors.White);
        if (keys[0].Time > 0.0001)
            animation.KeyFrames.Add(new LinearColorKeyFrame(Of(keys[0].Color), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        foreach (var key in keys)
        {
            var fraction = seconds <= 0.0001 ? 1 : Math.Clamp(key.Time / seconds, 0, 1);
            animation.KeyFrames.Add(new EasingColorKeyFrame(Of(key.Color), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(fraction * seconds)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        return animation;
    }

    /// <summary>Every painted property that belongs to one colour role.</summary>
    static IEnumerable<BrushSlot> Slots(Control root, MotionColorRole role, bool includeDescendants)
    {
        foreach (var element in ScopedDescendants(root, includeDescendants))
        {
            switch (role)
            {
                case MotionColorRole.Text:
                    if (element is TextBlock text)
                        yield return new BrushSlot(text, () => text.Foreground, brush => text.Foreground = brush, ForegroundSnapshots);
                    if (element is TextBox box)
                        yield return new BrushSlot(box, () => box.Foreground, brush => box.Foreground = brush, ForegroundSnapshots);
                    break;
                case MotionColorRole.Background:
                    if (element is Border border)
                        yield return new BrushSlot(border, () => border.Background, brush => border.Background = brush, BackgroundSnapshots);
                    if (element is Panel panel)
                        yield return new BrushSlot(panel, () => panel.Background, brush => panel.Background = brush, BackgroundSnapshots);
                    if (element is Shape shape)
                        yield return new BrushSlot(shape, () => shape.Fill, brush => shape.Fill = brush, BackgroundSnapshots);
                    break;
                case MotionColorRole.Border:
                    if (element is Border outlined)
                        yield return new BrushSlot(outlined, () => outlined.BorderBrush, brush => outlined.BorderBrush = brush, BorderSnapshots);
                    if (element is Shape stroked)
                        yield return new BrushSlot(stroked, () => stroked.Stroke, brush => stroked.Stroke = brush, BorderSnapshots);
                    break;
            }
        }
    }

    /// <summary>A painted property that can hold any brush, not just a solid one.</summary>
    sealed class BrushSlot
    {
        readonly AvaloniaObject _owner;
        readonly Func<IBrush?> _read;
        readonly Action<IBrush?> _assign;
        readonly ConditionalWeakTable<AvaloniaObject, BrushSnapshot> _snapshots;

        public BrushSlot(AvaloniaObject owner, Func<IBrush?> read, Action<IBrush?> assign,
            ConditionalWeakTable<AvaloniaObject, BrushSnapshot> snapshots)
        {
            _owner = owner;
            _read = read;
            _assign = assign;
            _snapshots = snapshots;
        }

        public void Set(Color color)
        {
            if (_read() is SolidColorBrush solid && !solid.IsFrozen)
            {
                solid.Color = color;
                return;
            }
            Remember();
            _assign(new SolidColorBrush(color));
        }

        public void SetOpacity(double opacity)
        {
            if (_read() is SolidColorBrush solid && !solid.IsFrozen)
            {
                solid.Opacity = Math.Clamp(opacity, 0, 1);
                return;
            }
            Remember();
            _assign(new SolidColorBrush(FirstColour(_read())) { Opacity = Math.Clamp(opacity, 0, 1) });
        }

        public void Animate(ColorAnimationUsingKeyFrames animation)
        {
            if (_read() is SolidColorBrush solid && !solid.IsFrozen)
            {
                RememberColour(solid);
                solid.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                return;
            }
            Remember();
            var live = new SolidColorBrush(FirstColour(_read()));
            _assign(live);
            RememberColour(live);
            live.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        public void AnimateOpacity(MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong)
        {
            if (_read() is SolidColorBrush solid && !solid.IsFrozen)
            {
                RememberOpacity(solid);
                var solidAnimation = BuildDouble(track, seconds, 1, 1, 1);
                solidAnimation.RepeatBehavior = repeat;
                solidAnimation.AutoReverse = pingPong;
                solid.BeginAnimation(Brush.OpacityProperty, solidAnimation);
                return;
            }
            Remember();
            var original = _read() as Brush;
            var live = new SolidColorBrush(FirstColour(original)) { Opacity = original?.Opacity ?? 1 };
            _assign(live);
            RememberOpacity(live);
            var liveAnimation = BuildDouble(track, seconds, 1, 1, 1);
            liveAnimation.RepeatBehavior = repeat;
            liveAnimation.AutoReverse = pingPong;
            live.BeginAnimation(Brush.OpacityProperty, liveAnimation);
        }

        public void Restore()
        {
            if (_read() is SolidColorBrush live) RestoreBrush(live);
            if (_snapshots.TryGetValue(_owner, out var snapshot))
            {
                _assign(snapshot.Brush);
                _snapshots.Remove(_owner);
            }
        }

        void Remember()
        {
            if (!_snapshots.TryGetValue(_owner, out _))
                _snapshots.Add(_owner, new BrushSnapshot(_read()));
        }

        static void RememberColour(SolidColorBrush brush)
        {
            if (!BrushColours.TryGetValue(brush, out _))
                BrushColours.Add(brush, new StrongBox<Color>(brush.Color));
        }

        static void RememberOpacity(Brush brush)
        {
            if (!BrushOpacities.TryGetValue(brush, out _))
                BrushOpacities.Add(brush, new StrongBox<double>(brush.Opacity));
        }

        static void RestoreBrush(SolidColorBrush brush)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.BeginAnimation(Brush.OpacityProperty, null);
            if (BrushColours.TryGetValue(brush, out var colour)) { brush.Color = colour.Value; BrushColours.Remove(brush); }
            if (BrushOpacities.TryGetValue(brush, out var opacity)) { brush.Opacity = opacity.Value; BrushOpacities.Remove(brush); }
        }

        static Color FirstColour(IBrush? brush) => brush switch
        {
            SolidColorBrush solid => solid.Color,
            GradientBrush gradient when gradient.GradientStops.Count > 0 => gradient.GradientStops[0].Color,
            _ => Colors.White
        };
    }

    sealed class BrushSnapshot
    {
        public IBrush? Brush { get; }
        public BrushSnapshot(IBrush? brush) => Brush = brush;
    }

    static IEnumerable<AvaloniaObject> ScopedDescendants(Control root, bool includeDescendants) =>
        includeDescendants ? Descendants(root) : new AvaloniaObject[] { root };

    static IEnumerable<AvaloniaObject> Descendants(AvaloniaObject root)
    {
        yield return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }
}
