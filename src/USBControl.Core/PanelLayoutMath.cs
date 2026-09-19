namespace USBControl.Core;

/// <summary>
/// Geometry for the free-form panel view: grid snapping, tile-overlap tests, and
/// deterministic auto-placement of tiles that the user has not positioned yet.
/// Pure math, unit-tested; the UI canvas uses these exact constants.
/// </summary>
public static class PanelLayoutMath
{
    public const double TileWidth = 238;
    public const double TileHeight = 130;
    public const double Gap = 12;
    public const double Margin = 8;
    public const double SnapGrid = 24;

    /// <summary>Logical canvas size (DIP). The XAML canvas must match.</summary>
    public const double CanvasWidth = 1400;
    public const double CanvasHeight = 900;

    /// <summary>Snaps to the grid and clamps horizontally into the canvas.</summary>
    public static double SnapX(double x) =>
        Math.Clamp(Math.Round(x / SnapGrid) * SnapGrid, 0, CanvasWidth - TileWidth - Margin);

    /// <summary>Snaps to the grid and clamps vertically into the canvas.</summary>
    public static double SnapY(double y) =>
        Math.Clamp(Math.Round(y / SnapGrid) * SnapGrid, 0, CanvasHeight - TileHeight - Margin);

    /// <summary>AABB overlap test with a one-gap breathing tolerance.</summary>
    public static bool Overlaps((double X, double Y) a, (double X, double Y) b)
    {
        const double step = TileWidth + Gap;
        const double stepY = TileHeight + Gap;
        return a.X < b.X + step && b.X < a.X + step
            && a.Y < b.Y + stepY && b.Y < a.Y + stepY;
    }

    /// <summary>
    /// Yields row-major free slots that don't collide with any occupied tile.
    /// Wraps to the next row when the current row runs out of width, scanning down
    /// the whole canvas before giving up.
    /// </summary>
    public static IEnumerable<(double X, double Y)> FreeSlots(IEnumerable<(double X, double Y)> occupied)
    {
        var occupiedList = occupied.ToList();
        var stepX = TileWidth + Gap;
        var stepY = TileHeight + Gap;
        var maxX = CanvasWidth - TileWidth - Margin;

        for (var y = Margin; y <= CanvasHeight - TileHeight - Margin; y += stepY)
        {
            for (var x = Margin; x <= maxX; x += stepX)
            {
                var slot = (x, y);
                if (occupiedList.Any(o => Overlaps(slot, o)))
                    continue;
                yield return slot;
            }
        }
    }

    /// <summary>Positions for <paramref name="keys"/> in the first free slots (stable order).</summary>
    public static Dictionary<string, (double X, double Y)> AutoPositions(
        IReadOnlyList<string> keys, IEnumerable<(double X, double Y)> occupied)
    {
        var result = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var slot in FreeSlots(occupied))
        {
            if (count >= keys.Count)
                break;
            result[keys[count]] = slot;
            count++;
        }
        return result;
    }
}
