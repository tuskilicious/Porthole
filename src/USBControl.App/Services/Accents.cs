using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace USBControl.App.Services;

/// <summary>
/// The neon accent catalog. Each palette recolors the whole UI (brand, focus, hover
/// glow, selection) while green/amber/red stay reserved for device state. Switching
/// replaces the "accent" ResourceDictionary with freshly built, frozen brushes.
///
/// Consumers must reference the accent resources with DynamicResource AT THE SETTER
/// level ({DynamicResource AccentBrush}) — never inside a Freezable (GradientStop,
/// SolidColorBrush, Effect), which WPF forbids and reports as a
/// "System.Windows.Media.GradientStop Color" parse error.
/// </summary>
public static class Accents
{
    public sealed record Palette(string Name, Color Base, Color Dim, Color Glow);

    public static readonly Palette Cyan = new("Cyan", Color.FromRgb(0x4C, 0xC2, 0xFF), Color.FromRgb(0x2A, 0x6E, 0x91), Color.FromRgb(0x1B, 0x45, 0x60));
    public static readonly Palette Violet = new("Violet", Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0x58, 0x49, 0x8F), Color.FromRgb(0x39, 0x2F, 0x60));
    public static readonly Palette Toxic = new("Toxic", Color.FromRgb(0xA3, 0xE6, 0x35), Color.FromRgb(0x55, 0x7A, 0x1D), Color.FromRgb(0x38, 0x51, 0x13));
    public static readonly Palette Blood = new("Blood", Color.FromRgb(0xFF, 0x46, 0x55), Color.FromRgb(0x8A, 0x26, 0x30), Color.FromRgb(0x5C, 0x19, 0x20));
    public static readonly Palette Amber = new("Amber", Color.FromRgb(0xFF, 0xB4, 0x54), Color.FromRgb(0x8F, 0x62, 0x2D), Color.FromRgb(0x5F, 0x41, 0x1E));

    public static readonly IReadOnlyList<Palette> All = new[] { Cyan, Violet, Toxic, Blood, Amber };

    public static Palette Resolve(string? name) =>
        All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Cyan;

    /// <summary>Applies the named palette (startup + live switching). Unknown names fall back to Cyan.</summary>
    public static void Apply(string? name)
    {
        var p = Resolve(name);
        var dict = new ResourceDictionary();

        void Brush(string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            dict[key] = b;
        }

        // Raw colors for anything Color-typed (storyboards etc.).
        dict["AccentColor"] = p.Base;
        dict["AccentDimColor"] = p.Dim;
        dict["AccentGlowColor"] = p.Glow;

        Brush("AccentBrush", p.Base);
        Brush("AccentDimBrush", p.Dim);
        Brush("AccentGlowBrush", p.Glow);

        // The wordmark/top-bar gradient: accent glow fading to transparent.
        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0));
        grad.GradientStops.Add(new GradientStop(p.Glow, 0.45));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
        grad.Freeze();
        dict["TopbarGradient"] = grad;

        var brand = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        brand.GradientStops.Add(new GradientStop(p.Base, 1.0));
        brand.GradientStops.Add(new GradientStop(p.Glow, 0.0));
        brand.Freeze();
        dict["BrandGradient"] = brand;

        Effect Glow(Color c, double radius, double opacity)
        {
            var e = new DropShadowEffect
            {
                Color = c,
                BlurRadius = radius,
                ShadowDepth = 0,
                Opacity = opacity,
            };
            e.Freeze();
            return e;
        }

        dict["AccentGlowEffect"] = Glow(p.Base, 16, 0.55);
        dict["AccentGlowStrongEffect"] = Glow(p.Base, 22, 0.8);

        var res = Application.Current.Resources.MergedDictionaries;
        var existing = res.FirstOrDefault(d => d.Contains("AccentBrush"));
        if (existing is not null)
            res.Remove(existing);
        res.Add(dict);
    }
}
