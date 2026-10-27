using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>Shared rendering rules so the studio and the audience output always agree.</summary>
internal static class ScorebugRender
{
    public static Brush ParseBrush(string value)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(value)!; }
        catch { return Brushes.Transparent; }
    }

    /// <summary>Background painted behind an element. Shapes/logo draw themselves, so their host stays clear.</summary>
    public static Brush HostBackground(ScorebugElement item) =>
        item.Kind is ScorebugElementKind.Shape or ScorebugElementKind.Logo || item.TransparentBackground
            ? Brushes.Transparent
            : ParseBrush(item.Background);

    public static FrameworkElement CreateContent(ScorebugElement item, Func<GameState>? state, MediaPlayback? playback = null, bool muteVideo = true, bool showVideoPlaceholder = false)
    {
        if (item.Kind == ScorebugElementKind.Video) return new VideoSurface(item, playback, muteVideo, showVideoPlaceholder);
        // Icons are drawn from live data rather than typed text: small bars per timeout, or a dot
        // showing which team has the ball.
        if (item.Kind == ScorebugElementKind.Text && item.Icon != ScorebugIcon.None) return new ScorebugIconView(item, state);
        if (item.Kind == ScorebugElementKind.Logo && !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
        {
            try { return new Image { Source = new BitmapImage(new Uri(item.ImagePath!)), Stretch = Stretch.Uniform }; }
            catch { /* fall through to placeholder text */ }
        }
        if (item.Kind == ScorebugElementKind.Shape) return CreateShape(item);
        var text = state is null ? item.Content : ScoreboardTemplates.Bind(item.Content, state(), item.PeriodFormat);
        return MakeText(item, text);
    }

    public static TextBlock MakeText(ScorebugElement item, string text) => new()
    {
        Text = text,
        Foreground = ParseBrush(item.Foreground),
        FontSize = item.FontSize,
        FontWeight = FontWeights.Bold,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    public static Shape CreateShape(ScorebugElement item)
    {
        Shape shape = item.Shape switch
        {
            ScorebugShape.Circle => new Ellipse(),
            ScorebugShape.Triangle => new Polygon { Points = new PointCollection { new(0.5, 0), new(1, 1), new(0, 1) } },
            _ => new Rectangle { RadiusX = 8, RadiusY = 8 }
        };
        shape.Stretch = Stretch.Fill;
        shape.Fill = ParseBrush(item.Background);
        shape.Stroke = ParseBrush(item.Foreground);
        shape.StrokeThickness = 2;
        return shape;
    }

    public static void ApplyToContent(FrameworkElement content, ScorebugElement item, string? text = null)
    {
        switch (content)
        {
            case ScorebugIconView icon:
                icon.Update(text);
                break;
            case TextBlock block:
                block.Text = text ?? item.Content;
                block.Foreground = ParseBrush(item.Foreground);
                block.FontSize = item.FontSize;
                break;
            case Shape shape:
                shape.Fill = ParseBrush(item.Background);
                shape.Stroke = ParseBrush(item.Foreground);
                break;
            case Image image when File.Exists(item.ImagePath ?? ""):
                try { image.Source = new BitmapImage(new Uri(item.ImagePath!)); } catch { }
                break;
        }
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
    readonly TextBlock? _text;
    readonly Ellipse? _dot;
    readonly StackPanel? _bars;

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
            _dot = new Ellipse { Fill = ScorebugRender.ParseBrush(item.Foreground), VerticalAlignment = VerticalAlignment.Center };
            Children.Add(_dot);
        }
        Update(text);
    }

    /// <summary>Copies the current game into the icon: bar count, dot side, and the text beside it.</summary>
    public void Update(string? text = null)
    {
        var state = _state?.Invoke();
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
                        Fill = ScorebugRender.ParseBrush(_item.Foreground)
                    });
            }
            return;
        }
        if (_dot is not null)
        {
            var size = Math.Max(8, Math.Min(_item.Width, _item.Height) * 0.3);
            _dot.Width = size;
            _dot.Height = size;
            _dot.Margin = new Thickness(size * 0.35, 0, size * 0.35, 0);
            _dot.Fill = ScorebugRender.ParseBrush(_item.Foreground);
            // Visitors on the left, home on the right, like the console; the element can mirror it.
            var home = state?.HomePossession ?? true;
            var right = _item.IconFlipped ? !home : home;
            _dot.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        if (_text is not null)
        {
            var value = text ?? (state is null ? _item.Content : ScoreboardTemplates.Bind(_item.Content, state, _item.PeriodFormat));
            if (_text.Text != value) _text.Text = value;
        }
    }
}
