using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>Shared rendering rules so the studio and the audience output always agree.</summary>
internal static class ScorebugRender
{
    public static Brush ParseBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Brushes.Transparent;
        try { return (Brush)new BrushConverter().ConvertFromString(value)!; }
        catch { return Brushes.Transparent; }
    }

    /// <summary>The colour an element actually draws, after its team-palette slot is resolved.</summary>
    public static string ResolveForeground(ScorebugElement item, GameState? state) =>
        state?.ColorFor(item.ForegroundSource) ?? item.Foreground;

    public static string ResolveBackground(ScorebugElement item, GameState? state) =>
        state?.ColorFor(item.BackgroundSource) ?? item.Background;

    /// <summary>Background painted behind an element. Shapes/logo draw themselves, so their host stays clear.</summary>
    public static Brush HostBackground(ScorebugElement item, GameState? state = null) =>
        item.Kind is ScorebugElementKind.Shape or ScorebugElementKind.Logo || item.TransparentBackground
            ? Brushes.Transparent
            : ParseBrush(ResolveBackground(item, state));

    public static string? ResolveLogoPath(ScorebugElement item, GameState? state) =>
        item.LogoSide == ScorebugLogoSide.None ? item.ImagePath : state?.LogoFor(item.LogoSide) ?? item.ImagePath;

    public static FrameworkElement CreateContent(ScorebugElement item, Func<GameState>? state, MediaPlayback? playback = null, bool muteVideo = true, bool showVideoPlaceholder = false)
    {
        if (item.Kind == ScorebugElementKind.Video) return new VideoSurface(item, playback, muteVideo, showVideoPlaceholder);
        // Icons are drawn from live data rather than typed text: small bars per timeout, or a dot
        // showing which team has the ball.
        if (item.Kind == ScorebugElementKind.Text && item.Icon != ScorebugIcon.None) return new ScorebugIconView(item, state);
        if (item.Kind == ScorebugElementKind.Logo)
        {
            var path = ResolveLogoPath(item, state?.Invoke());
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try { return new Image { Source = LoadLogo(path), Stretch = Stretch.Uniform }; }
                catch { /* fall through to placeholder text */ }
            }
            // A team logo that has not been set yet is labelled in the studio and blank on air.
            return showVideoPlaceholder
                ? MakeText(item, item.LogoSide == ScorebugLogoSide.None ? item.Content : $"{item.LogoSide.ToString().ToUpperInvariant()} LOGO")
                : new Grid();
        }
        if (item.Kind == ScorebugElementKind.Shape) return CreateShape(item, state?.Invoke());
        var text = state is null ? item.Content : ScoreboardTemplates.Bind(item.Content, state(), item.PeriodFormat);
        return MakeText(item, text);
    }

    /// <summary>Logos are copied into the app folder, but they still need a caching load.</summary>
    static BitmapImage LoadLogo(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static ScorebugTextBlock MakeText(ScorebugElement item, string text)
    {
        var block = new ScorebugTextBlock(item);
        block.UpdateText(text, ScorebugTransition.Cut);
        return block;
    }

    public static Shape CreateShape(ScorebugElement item, GameState? state = null)
    {
        Shape shape = item.Shape switch
        {
            ScorebugShape.Circle => new Ellipse(),
            ScorebugShape.Triangle => new Polygon { Points = new PointCollection { new(0.5, 0), new(1, 1), new(0, 1) } },
            _ => new Rectangle { RadiusX = 8, RadiusY = 8 }
        };
        shape.Stretch = Stretch.Fill;
        shape.Fill = ParseBrush(ResolveBackground(item, state));
        shape.Stroke = ParseBrush(ResolveForeground(item, state));
        shape.StrokeThickness = 2;
        return shape;
    }

    /// <summary>Applies text, colour, font and style to whatever content an element created.</summary>
    public static void ApplyToContent(FrameworkElement content, ScorebugElement item, GameState? state = null, string? text = null)
    {
        switch (content)
        {
            case ScorebugIconView icon:
                icon.Update(text);
                break;
            case ScorebugTextBlock block:
                block.ApplyStyle(item, state);
                block.UpdateText(text ?? item.Content, item.Transition);
                break;
            case TextBlock plain:
                plain.Text = text ?? item.Content;
                break;
            case Shape shape:
                shape.Fill = ParseBrush(ResolveBackground(item, state));
                shape.Stroke = ParseBrush(ResolveForeground(item, state));
                break;
            case Image image:
                var path = ResolveLogoPath(item, state);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    try { image.Source = LoadLogo(path); } catch { }
                }
                else
                {
                    // Removing a team logo must take it off the graphic now, not after a rebuild.
                    image.Source = null;
                }
                break;
        }
    }
}

/// <summary>
/// A text element that knows how it should cross from one value to the next. A score that merely
/// cuts feels like a spreadsheet; a wheel, a dissolve or a flip feels like television. Values that
/// change while an animation is still running snap to the newest one so the graphic is never stale.
/// </summary>
public sealed class ScorebugTextBlock : TextBlock
{
    readonly TranslateTransform _translate = new();
    readonly ScaleTransform _scale = new(1, 1);
    string _shown = "";
    bool _animating;
    // A transition is two phases with callbacks; a newer value (or a snap) bumps the generation,
    // so a stale out-phase can never put its old text back on the graphic.
    int _generation;
    ScorebugElement? _style;

    public ScorebugTextBlock(ScorebugElement item)
    {
        _style = item;
        TextAlignment = TextAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
        TextWrapping = TextWrapping.Wrap;
        RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_translate);
        RenderTransform = group;
        ApplyStyle(item, null);
    }

    public void ApplyStyle(ScorebugElement item, GameState? state)
    {
        FontFamily = FontCatalog.Resolve(item.FontFamily);
        FontSize = item.FontSize;
        FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal;
        Foreground = ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item, state));
        var decorations = new TextDecorationCollection();
        if (item.Underline) decorations.Add(System.Windows.TextDecorations.Underline[0]);
        if (item.StrikeThrough) decorations.Add(System.Windows.TextDecorations.Strikethrough[0]);
        TextDecorations = decorations.Count == 0 ? null : decorations;
        _style = item;
    }

    /// <summary>Shows a new value, crossing to it with the element's chosen animation.</summary>
    public void UpdateText(string text, ScorebugTransition transition)
    {
        text ??= "";
        if (_shown == text && !_animating) return;
        if (transition == ScorebugTransition.Cut || !IsLoaded || _animating)
        {
            Snap(text);
            return;
        }
        _animating = true;
        _shown = text;
        var generation = _generation;
        var duration = OutDuration(transition);
        PlayOut(transition, duration, () =>
        {
            if (generation != _generation) return;
            Text = text;
            PlayIn(transition, InDuration(transition), () =>
            {
                if (generation == _generation) _animating = false;
            });
        });
    }

    void Snap(string text)
    {
        _generation++;
        BeginAnimation(OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _translate.BeginAnimation(TranslateTransform.XProperty, null);
        _translate.BeginAnimation(TranslateTransform.YProperty, null);
        Opacity = 1;
        _scale.ScaleX = _scale.ScaleY = 1;
        _translate.X = _translate.Y = 0;
        Text = text;
        _shown = text;
        _animating = false;
    }

    double Distance => Math.Max(8, FontSize * 1.15);

    static TimeSpan OutDuration(ScorebugTransition transition) => transition switch
    {
        ScorebugTransition.Pop => TimeSpan.FromMilliseconds(90),
        ScorebugTransition.Flip => TimeSpan.FromMilliseconds(130),
        _ => TimeSpan.FromMilliseconds(120)
    };

    static TimeSpan InDuration(ScorebugTransition transition) => transition switch
    {
        ScorebugTransition.Pop => TimeSpan.FromMilliseconds(220),
        ScorebugTransition.Drop => TimeSpan.FromMilliseconds(190),
        _ => TimeSpan.FromMilliseconds(160)
    };

    // ---------- out ----------

    void PlayOut(ScorebugTransition transition, TimeSpan duration, Action done)
    {
        switch (transition)
        {
            case ScorebugTransition.Fade:
                Animate(1, 0, duration, done);
                break;
            case ScorebugTransition.FadeScale:
                Animate(1, 0, duration, done, scale: 0.94);
                break;
            case ScorebugTransition.SlideUp:
                Animate(1, 0.35, duration, done, y: -Distance * 0.6);
                break;
            case ScorebugTransition.SlideDown:
                Animate(1, 0.35, duration, done, y: Distance * 0.6);
                break;
            case ScorebugTransition.SlideLeft:
                Animate(1, 0.35, duration, done, x: -Distance * 0.6);
                break;
            case ScorebugTransition.SlideRight:
                Animate(1, 0.35, duration, done, x: Distance * 0.6);
                break;
            case ScorebugTransition.WheelUp:
                // No fade: the old value rolls out like a drum, and the new one rolls in behind it.
                Animate(1, 1, duration, done, y: -Distance);
                break;
            case ScorebugTransition.WheelDown:
                Animate(1, 1, duration, done, y: Distance);
                break;
            case ScorebugTransition.Flip:
                Animate(1, 0.1, duration, done, scaleY: 0);
                break;
            case ScorebugTransition.Zoom:
                Animate(1, 0, duration, done, scale: 0.6);
                break;
            case ScorebugTransition.Pop:
                Animate(1, 0.2, duration, done, scale: 0.86);
                break;
            case ScorebugTransition.Drop:
                Animate(1, 0.25, duration, done, y: -Distance * 0.55);
                break;
            default:
                Animate(1, 1, duration, done);
                break;
        }
    }

    // ---------- in ----------

    void PlayIn(ScorebugTransition transition, TimeSpan duration, Action done)
    {
        switch (transition)
        {
            case ScorebugTransition.Fade:
                Animate(0, 1, duration, done);
                break;
            case ScorebugTransition.FadeScale:
                Animate(0, 1, duration, done, scale: 0.94, snapScale: true);
                break;
            case ScorebugTransition.SlideUp:
                Animate(0, 1, duration, done, y: Distance * 0.5, clearOffset: true);
                break;
            case ScorebugTransition.SlideDown:
                Animate(0, 1, duration, done, y: -Distance * 0.5, clearOffset: true);
                break;
            case ScorebugTransition.SlideLeft:
                Animate(0, 1, duration, done, x: Distance * 0.5, clearOffset: true);
                break;
            case ScorebugTransition.SlideRight:
                Animate(0, 1, duration, done, x: -Distance * 0.5, clearOffset: true);
                break;
            case ScorebugTransition.WheelUp:
                Animate(1, 1, duration, done, y: Distance, clearOffset: true);
                break;
            case ScorebugTransition.WheelDown:
                Animate(1, 1, duration, done, y: -Distance, clearOffset: true);
                break;
            case ScorebugTransition.Flip:
                Animate(1, 1, duration, done, fromScaleY: 0.1);
                break;
            case ScorebugTransition.Zoom:
                Animate(0, 1, duration, done, scale: 0.6, snapScale: true);
                break;
            case ScorebugTransition.Pop:
                Animate(0, 1, duration, done, scale: 0.86, snapScale: true, overshoot: true);
                break;
            case ScorebugTransition.Drop:
                Animate(0, 1, duration, done, y: -Distance * 0.5, clearOffset: true);
                break;
            default:
                Animate(1, 1, duration, done);
                break;
        }
    }

    /// <summary>
    /// Runs one phase. The opacity animation is always present, which gives a reliable completion
    /// callback even for a wheel that only moves.
    /// </summary>
    void Animate(double fromOpacity, double toOpacity, TimeSpan duration, Action done,
        double? x = null, double? y = null, double? scale = null, double? scaleY = null, double? fromScaleY = null,
        bool clearOffset = false, bool snapScale = false, bool overshoot = false)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(fromOpacity, toOpacity, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        var finished = false;
        fade.Completed += (_, _) =>
        {
            if (finished) return;
            finished = true;
            done();
        };
        BeginAnimation(OpacityProperty, fade);

        if (clearOffset)
        {
            _translate.X = x ?? 0;
            _translate.Y = y ?? 0;
            _translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_translate.X, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_translate.Y, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
        else if (x is double || y is double)
        {
            _translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x ?? 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y ?? 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }

        if (snapScale && scale is double startScale)
        {
            _scale.ScaleX = _scale.ScaleY = startScale;
            ScaleTo(1, duration, ease);
        }
        else if (scale is double endScale)
        {
            ScaleTo(endScale, duration, ease);
        }

        if (fromScaleY is double startScaleY)
        {
            _scale.ScaleY = startScaleY;
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(startScaleY, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
        else if (scaleY is double endScaleY)
        {
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(endScaleY, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }

        if (overshoot)
        {
            // A pop lands a touch too big and settles: the little bounce that reads as "new value".
            var pop = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromPercent(0.65), new CubicEase { EasingMode = EasingMode.EaseOut }));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

    }

    void ScaleTo(double endScale, TimeSpan duration, IEasingFunction ease)
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(endScale, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(endScale, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
}

/// <summary>
/// An element that draws live artwork instead of (or alongside) its text: one little bar per
/// timeout the team has left, or a dot on the side of the team with the ball.
/// </summary>
public sealed class ScorebugIconView : Grid
{
    readonly ScorebugElement _item;
    readonly Func<GameState>? _state;
    readonly ScorebugTextBlock? _text;
    readonly Ellipse? _dot;
    readonly StackPanel? _bars;
    string _appliedForeground = "";

    public ScorebugIconView(ScorebugElement item, Func<GameState>? state, string? text = null)
    {
        _item = item;
        _state = state;
        ClipToBounds = true;
        if (item.Icon == ScorebugIcon.TimeoutBars)
        {
            // One short horizontal line per timeout, in a row from left to right.
            _bars = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Children.Add(_bars);
        }
        else
        {
            // The dot sits beside whatever the element says (usually {{POSSESSION}}).
            if (!string.IsNullOrWhiteSpace(item.Content))
            {
                _text = ScorebugRender.MakeText(item, text ?? item.Content);
                Children.Add(_text);
            }
            _dot = new Ellipse { Fill = ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item, state?.Invoke())), VerticalAlignment = VerticalAlignment.Center };
            Children.Add(_dot);
        }
        Update(text);
    }

    /// <summary>Copies the current game into the icon: bar count, dot side, and the text beside it.</summary>
    public void Update(string? text = null)
    {
        var state = _state?.Invoke();
        _text?.ApplyStyle(_item, state);
        var foreground = ScorebugRender.ResolveForeground(_item, state);
        if (_bars is not null)
        {
            var count = 0;
            if (state is not null) count = Math.Clamp(_item.IconVisitors ? state.AwayTimeouts : state.HomeTimeouts, 0, 10);
            if (_bars.Children.Count != count)
            {
                _bars.Children.Clear();
                var slot = _item.Width / Math.Max(1, count);
                var width = Math.Max(6, Math.Min(_item.Width * 0.22, slot * 0.62));
                var thickness = Math.Clamp(_item.Height / 8, 2, 6);
                var gap = Math.Max(2, width * 0.22);
                for (var i = 0; i < count; i++)
                    _bars.Children.Add(new Rectangle
                    {
                        Width = width,
                        Height = thickness,
                        RadiusX = thickness / 2,
                        RadiusY = thickness / 2,
                        Margin = new Thickness(gap / 2, 0, gap / 2, 0),
                        Fill = ScorebugRender.ParseBrush(foreground)
                    });
                _appliedForeground = foreground;
            }
            else if (foreground != _appliedForeground)
            {
                // A team colour change repaints the existing bars instead of redrawing them.
                var brush = ScorebugRender.ParseBrush(foreground);
                foreach (var child in _bars.Children.OfType<Rectangle>()) child.Fill = brush;
                _appliedForeground = foreground;
            }
            return;
        }
        if (_dot is not null)
        {
            var size = Math.Max(8, Math.Min(_item.Width, _item.Height) * 0.3);
            _dot.Width = size;
            _dot.Height = size;
            _dot.Margin = new Thickness(size * 0.35, 0, size * 0.35, 0);
            _dot.Fill = ScorebugRender.ParseBrush(foreground);
            // Visitors on the left, home on the right, like the console; the element can mirror it.
            var home = state?.HomePossession ?? true;
            var right = _item.IconFlipped ? !home : home;
            _dot.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        if (_text is not null)
        {
            var value = text ?? (state is null ? _item.Content : ScoreboardTemplates.Bind(_item.Content, state, _item.PeriodFormat));
            _text.UpdateText(value, _item.Transition);
        }
    }
}
