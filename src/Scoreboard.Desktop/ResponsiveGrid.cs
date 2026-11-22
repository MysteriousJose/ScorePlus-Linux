using System;
using Avalonia;
using Avalonia.Controls;

namespace Scoreboard.Desktop;

/// <summary>
/// Lays its children out in equal cells whose column count follows the available width, so a card
/// row folds from four across to two to one instead of squeezing its cards past each other. The
/// ladder is counted down by halving from <see cref="MaxColumns"/> — 4, 2, 1 — and a count is only
/// used when every cell still gets at least <see cref="MinItemWidth"/>.
/// </summary>
public class ResponsiveGrid : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<ResponsiveGrid, double>(nameof(MinItemWidth), 240);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<ResponsiveGrid, int>(nameof(MaxColumns), 4);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<ResponsiveGrid, double>(nameof(RowSpacing), 6);

    static ResponsiveGrid()
    {
        AffectsMeasure<ResponsiveGrid>(MinItemWidthProperty, MaxColumnsProperty, RowSpacingProperty);
        AffectsArrange<ResponsiveGrid>(MinItemWidthProperty, MaxColumnsProperty, RowSpacingProperty);
    }

    /// <summary>The narrowest a cell may get before the row drops to a shorter column ladder.</summary>
    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>The most columns the row uses on a wide window: four, two or one.</summary>
    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>The gap between rows; the cards carry their own left/right margins.</summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>How many columns fit at this width, snapped to the 4/2/1 ladder.</summary>
    internal int ColumnsFor(double width)
    {
        var max = Math.Max(1, MaxColumns);
        var minimum = Math.Max(80, MinItemWidth);
        if (!double.IsFinite(width) || width <= 0) return max;
        var fit = Math.Clamp((int)Math.Floor(width / minimum), 1, max);
        for (var columns = max; columns >= 1; columns /= 2)
            if (columns <= fit) return columns;
        return 1;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        if (count == 0) return default;

        var width = availableSize.Width;
        if (!double.IsFinite(width) || width <= 0)
            width = Math.Max(1, MaxColumns) * Math.Max(80, MinItemWidth);
        var columns = ColumnsFor(width);
        var cell = width / columns;
        var rows = (int)Math.Ceiling(count / (double)columns);
        var heights = new double[rows];
        for (var i = 0; i < count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(cell, double.PositiveInfinity));
            heights[i / columns] = Math.Max(heights[i / columns], child.DesiredSize.Height);
        }

        var height = 0.0;
        foreach (var rowHeight in heights) height += rowHeight;
        height += Math.Max(0, RowSpacing) * Math.Max(0, rows - 1);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = Children.Count;
        if (count == 0) return finalSize;

        var width = finalSize.Width;
        if (!double.IsFinite(width) || width <= 0)
            width = Math.Max(1, MaxColumns) * Math.Max(80, MinItemWidth);
        var columns = ColumnsFor(width);
        var cell = width / columns;
        var rows = (int)Math.Ceiling(count / (double)columns);
        var heights = new double[rows];
        for (var i = 0; i < count; i++)
            heights[i / columns] = Math.Max(heights[i / columns], Children[i].DesiredSize.Height);

        var spacing = Math.Max(0, RowSpacing);
        var used = spacing * Math.Max(0, rows - 1);
        foreach (var rowHeight in heights) used += rowHeight;

        // Fill a taller slot the way the star rows this panel replaces did, so cards stretch with
        // the window instead of leaving a band of dead space underneath them.
        var extra = finalSize.Height - used;
        if (extra > 0.5)
            for (var row = 0; row < rows; row++) heights[row] += extra / rows;

        var y = 0.0;
        for (var i = 0; i < count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            Children[i].Arrange(new Rect(column * cell, y, cell, heights[row]));
            if (column == columns - 1 || i == count - 1) y += heights[row] + spacing;
        }
        return finalSize;
    }
}
