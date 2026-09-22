using System.Windows;
using System.Windows.Media;

namespace USBControl.App.Services;

/// <summary>
/// The base-palette catalog (Dark / Light): background, surface, border, text and
/// device-state colors. Mirrors <see cref="Accents"/>'s exact technique — a named
/// palette rebuilds a frozen ResourceDictionary and swaps it into
/// <c>Application.Current.Resources.MergedDictionaries</c> — but for the page
/// canvas instead of the accent. See Theme.xaml's header for why a few brushes
/// here (TileFillBrush, TileFillHoverBrush, TileHighlightBrush, PanelGridBrush)
/// are built whole in code: their color inputs live inside a Freezable
/// (GradientStop, GeometryDrawing), which cannot itself take a DynamicResource.
///
/// Consumers reference every key here with DynamicResource at the setter level,
/// same rule as Accents.
/// </summary>
public static class ThemeMode
{
    public sealed record Palette(
        string Name,
        Color Bg, Color Surface, Color Raised, Color Border, Color BorderStrong,
        Color TextPrimary, Color TextDim, Color TextMuted, Color TextOnAccent,
        Color Good, Color GoodSoft, Color Warn, Color WarnSoft, Color Bad, Color BadSoft,
        Color NeutralSoft, Color ToggleOff,
        Color TileTop, Color TileBottom, Color TileTopHover, Color TileBottomHover,
        Color TileHighlightTint);

    public static readonly Palette Dark = new(
        "Dark",
        Bg: Color.FromRgb(0x0B, 0x0D, 0x10), Surface: Color.FromRgb(0x14, 0x17, 0x1C),
        Raised: Color.FromRgb(0x1B, 0x1F, 0x26), Border: Color.FromRgb(0x25, 0x2A, 0x33),
        BorderStrong: Color.FromRgb(0x36, 0x3D, 0x49),
        TextPrimary: Color.FromRgb(0xE8, 0xEA, 0xED), TextDim: Color.FromRgb(0x9A, 0xA3, 0xAE),
        TextMuted: Color.FromRgb(0x5F, 0x68, 0x73), TextOnAccent: Color.FromRgb(0x0B, 0x0D, 0x10),
        Good: Color.FromRgb(0x3D, 0xD6, 0x8C), GoodSoft: Color.FromArgb(0x24, 0x3D, 0xD6, 0x8C),
        Warn: Color.FromRgb(0xF5, 0xA5, 0x24), WarnSoft: Color.FromArgb(0x24, 0xF5, 0xA5, 0x24),
        Bad: Color.FromRgb(0xF0, 0x50, 0x6E), BadSoft: Color.FromArgb(0x24, 0xF0, 0x50, 0x6E),
        NeutralSoft: Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF),
        ToggleOff: Color.FromRgb(0x3A, 0x41, 0x4C),
        TileTop: Color.FromRgb(0x16, 0x1A, 0x20), TileBottom: Color.FromRgb(0x12, 0x15, 0x1A),
        TileTopHover: Color.FromRgb(0x1D, 0x22, 0x2A), TileBottomHover: Color.FromRgb(0x17, 0x1B, 0x21),
        TileHighlightTint: Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// The mockup's light theme: cream page, ink text. Its own convention — mint marks
    /// a tile's "powered on" state wherever the toggle icon appears, broader than the
    /// Dark theme's active-profile-only rule (see Theme.xaml header) — lives in the
    /// tile styling that reads <see cref="ProfileActiveBrush"/> directly for that icon,
    /// not in this palette (mint is a fixed brand color, not theme-swapped).
    /// </summary>
    public static readonly Palette Light = new(
        "Light",
        Bg: Color.FromRgb(0xF4, 0xF1, 0xEA), Surface: Color.FromRgb(0xFF, 0xFD, 0xF8),
        Raised: Color.FromRgb(0xFF, 0xFF, 0xFF), Border: Color.FromRgb(0xE3, 0xDD, 0xD0),
        BorderStrong: Color.FromRgb(0xCF, 0xC7, 0xB4),
        TextPrimary: Color.FromRgb(0x13, 0x15, 0x19), TextDim: Color.FromRgb(0x5C, 0x5A, 0x52),
        TextMuted: Color.FromRgb(0x93, 0x8F, 0x82), TextOnAccent: Color.FromRgb(0xFF, 0xFF, 0xFF),
        Good: Color.FromRgb(0x2F, 0xAE, 0x7A), GoodSoft: Color.FromArgb(0x24, 0x2F, 0xAE, 0x7A),
        Warn: Color.FromRgb(0xD6, 0x81, 0x1F), WarnSoft: Color.FromArgb(0x24, 0xD6, 0x81, 0x1F),
        Bad: Color.FromRgb(0xD5, 0x38, 0x4F), BadSoft: Color.FromArgb(0x24, 0xD5, 0x38, 0x4F),
        NeutralSoft: Color.FromArgb(0x14, 0x13, 0x15, 0x19),
        ToggleOff: Color.FromRgb(0xD8, 0xD2, 0xC4),
        TileTop: Color.FromRgb(0xFF, 0xFF, 0xFF), TileBottom: Color.FromRgb(0xF7, 0xF3, 0xEB),
        TileTopHover: Color.FromRgb(0xFF, 0xFF, 0xFF), TileBottomHover: Color.FromRgb(0xFB, 0xF8, 0xF1),
        TileHighlightTint: Color.FromArgb(0x0F, 0x00, 0x00, 0x00));

    public static readonly IReadOnlyList<Palette> All = new[] { Dark, Light };

    public static Palette Resolve(string? name) =>
        All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Dark;

    /// <summary>The palette last passed to <see cref="Apply"/> — read by anything that can't use
    /// DynamicResource (e.g. KindToPeripheralIconConverter picking an icon variant file).</summary>
    public static string CurrentName { get; private set; } = Dark.Name;

    /// <summary>Applies the named palette (startup + live switching). Unknown names fall back to Dark.</summary>
    public static void Apply(string? name)
    {
        var p = Resolve(name);
        CurrentName = p.Name;
        var dict = new ResourceDictionary();

        void Brush(string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            dict[key] = b;
        }

        // Raw colors for anything Color-typed (DarkTitleBar's DWM caption/border/text calls,
        // DemoMode's rendered demo photo).
        dict["BgColor"] = p.Bg;
        dict["BorderColor"] = p.Border;
        dict["TextPrimaryColor"] = p.TextPrimary;
        dict["RaisedColor"] = p.Raised;

        Brush("BgBrush", p.Bg);
        Brush("SurfaceBrush", p.Surface);
        Brush("RaisedBrush", p.Raised);
        Brush("BorderBrush", p.Border);
        Brush("BorderStrongBrush", p.BorderStrong);
        Brush("TextBrush", p.TextPrimary);
        Brush("TextDimBrush", p.TextDim);
        Brush("TextMutedBrush", p.TextMuted);
        Brush("TextOnAccentBrush", p.TextOnAccent);
        Brush("GoodBrush", p.Good);
        Brush("GoodSoftBrush", p.GoodSoft);
        Brush("WarnBrush", p.Warn);
        Brush("WarnSoftBrush", p.WarnSoft);
        Brush("BadBrush", p.Bad);
        Brush("BadSoftBrush", p.BadSoft);
        Brush("NeutralSoftBrush", p.NeutralSoft);
        Brush("ToggleOffBrush", p.ToggleOff);

        // Whole-brush resources: their color inputs live inside a Freezable
        // (GradientStop.Color, GeometryDrawing.Brush), which cannot take a
        // DynamicResource — see Theme.xaml's header and Accents.cs's own rule.
        LinearGradientBrush TileFill(Color top, Color bottom)
        {
            var b = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
            b.Freeze();
            return b;
        }
        dict["TileFillBrush"] = TileFill(p.TileTop, p.TileBottom);
        dict["TileFillHoverBrush"] = TileFill(p.TileTopHover, p.TileBottomHover);

        var highlight = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        var transparentTint = Color.FromArgb(0x00, p.TileHighlightTint.R, p.TileHighlightTint.G, p.TileHighlightTint.B);
        highlight.GradientStops.Add(new GradientStop(transparentTint, 0));
        highlight.GradientStops.Add(new GradientStop(p.TileHighlightTint, 0.5));
        highlight.GradientStops.Add(new GradientStop(transparentTint, 1));
        highlight.Freeze();
        dict["TileHighlightBrush"] = highlight;

        var grid = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 24, 24),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 24, 24),
            Drawing = new GeometryDrawing(new SolidColorBrush(p.Border), null, new RectangleGeometry(new Rect(0, 0, 1.5, 1.5))),
        };
        grid.Freeze();
        dict["PanelGridBrush"] = grid;

        var res = Application.Current.Resources.MergedDictionaries;
        var existing = res.FirstOrDefault(d => d.Contains("BgBrush"));
        if (existing is not null)
            res.Remove(existing);
        res.Add(dict);
    }
}
