using System.Windows;
using System.Windows.Controls;

namespace USBControl.App.Services;

/// <summary>
/// A wrap layout that fills the available width: it computes how many columns of at
/// least <see cref="MinTileWidth"/> fit, then stretches every tile in the row to
/// (available / columns), capped at <see cref="MaxTileWidth"/> so tiles don't balloon
/// on ultra-wide windows. All rows get a uniform height (the tallest tile), so the
/// grid stays flush like a HUD dashboard.
/// </summary>
public sealed class ResponsiveTilePanel : Panel
{
    public static readonly DependencyProperty MinTileWidthProperty = DependencyProperty.Register(
        nameof(MinTileWidth), typeof(double), typeof(ResponsiveTilePanel),
        new FrameworkPropertyMetadata(224.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxTileWidthProperty = DependencyProperty.Register(
        nameof(MaxTileWidth), typeof(double), typeof(ResponsiveTilePanel),
        new FrameworkPropertyMetadata(340.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Horizontal gap between columns (children usually carry a right-margin too; this is on top).</summary>
    public static readonly DependencyProperty GapXProperty = DependencyProperty.Register(
        nameof(GapX), typeof(double), typeof(ResponsiveTilePanel),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Vertical gap between rows.</summary>
    public static readonly DependencyProperty GapYProperty = DependencyProperty.Register(
        nameof(GapY), typeof(double), typeof(ResponsiveTilePanel),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinTileWidth
    {
        get => (double)GetValue(MinTileWidthProperty);
        set => SetValue(MinTileWidthProperty, value);
    }

    public double MaxTileWidth
    {
        get => (double)GetValue(MaxTileWidthProperty);
        set => SetValue(MaxTileWidthProperty, value);
    }

    public double GapX
    {
        get => (double)GetValue(GapXProperty);
        set => SetValue(GapXProperty, value);
    }

    public double GapY
    {
        get => (double)GetValue(GapYProperty);
        set => SetValue(GapYProperty, value);
    }

    private record struct GridLayout(int Columns, double TileWidth);

    /// <summary>Picks the column count and per-tile width for a given available width.</summary>
    private GridLayout Solve(double availableWidth)
    {
        var min = Math.Max(1, MinTileWidth);
        var max = Math.Max(min, MaxTileWidth);
        var gap = Math.Max(0, GapX);

        // How many min-width columns (with gaps) fit? At least one, always.
        var columns = Math.Max(1, (int)Math.Floor((availableWidth + gap) / (min + gap)));

        // Stretch tiles to fill the row, but never past the max.
        var tileWidth = Math.Min(max, (availableWidth - gap * (columns - 1)) / columns);
        return new GridLayout(columns, Math.Max(1, tileWidth));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinTileWidth : availableSize.Width;
        var layout = Solve(width);

        var childConstraint = new Size(layout.TileWidth, double.PositiveInfinity);
        double rowHeight = 0, totalHeight = 0, columnsInRow = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childConstraint);
            var h = child.DesiredSize.Height;
            if (columnsInRow == layout.Columns)
            {
                totalHeight += rowHeight + GapY;
                rowHeight = 0;
                columnsInRow = 0;
            }
            rowHeight = Math.Max(rowHeight, h);
            columnsInRow++;
        }
        totalHeight += rowHeight;

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = Solve(finalSize.Width);

        // Pass 1: settle each row's height first (tallest tile wins) so every tile
        // in the row is arranged with the final height, not a growing one.
        var rowHeights = new List<double>();
        double rowHeight = 0;
        var inRow = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (inRow == layout.Columns)
            {
                rowHeights.Add(rowHeight);
                rowHeight = 0;
                inRow = 0;
            }
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            inRow++;
        }
        if (inRow > 0)
            rowHeights.Add(rowHeight);

        // Pass 2: arrange.
        double x = 0, y = 0;
        var column = 0;
        var row = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (column == layout.Columns)
            {
                column = 0;
                x = 0;
                y += rowHeights[row] + GapY;
                row++;
            }

            child.Arrange(new Rect(x, y, layout.TileWidth, rowHeights[row]));
            x += layout.TileWidth + GapX;
            column++;
        }

        return finalSize;
    }
}
