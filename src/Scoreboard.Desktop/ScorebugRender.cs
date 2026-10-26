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

    public static FrameworkElement CreateContent(ScorebugElement item, Func<GameState>? state)
    {
        if (item.Kind == ScorebugElementKind.Logo && !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
        {
            try { return new Image { Source = new BitmapImage(new Uri(item.ImagePath!)), Stretch = Stretch.Uniform }; }
            catch { /* fall through to placeholder text */ }
        }
        if (item.Kind == ScorebugElementKind.Shape) return CreateShape(item);
        var text = state is null ? item.Content : ScoreboardTemplates.Bind(item.Content, state());
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
