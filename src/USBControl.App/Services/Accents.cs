using System.Windows;
using System.Windows.Media;

namespace USBControl.App.Services;

/// <summary>
/// The neon accent catalog. Each palette recolors the whole UI (brand, focus, hover
/// glow, selection) while green/amber/red stay reserved for device state. Switching
/// swaps the "AccentBrushes" ResourceDictionary in Application.Resources live.
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

        void Put(string key, Color c) => dict[key] = new SolidColorBrush(c);

        Put("AccentColor", p.Base);
        Put("AccentDimColor", p.Dim);
        Put("AccentGlowColor", p.Glow);

        var accent = (SolidColorBrush)dict["AccentColor"];
        accent.Freeze();
        var dim = (SolidColorBrush)dict["AccentDimColor"];
        dim.Freeze();
        var glow = (SolidColorBrush)dict["AccentGlowColor"];
        glow.Freeze();

        // The wordmark gradient: accent fades into the panel background.
        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        grad.GradientStops.Add(new GradientStop(p.Base, 1.0));
        grad.GradientStops.Add(new GradientStop(p.Glow, 0.0));
        grad.Freeze();
        dict["BrandGradient"] = grad;

        var res = Application.Current.Resources.MergedDictionaries;
        var existing = res.FirstOrDefault(d => d.Contains("AccentColor") && d.Source?.OriginalString.Contains("Accent") == true);
        if (existing is not null)
            res.Remove(existing);
        res.Add(dict);
    }
}
