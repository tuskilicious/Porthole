using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace USBControl.App.Services;

/// <summary>
/// Paints the native title bar to match the app: immersive dark mode plus a caption
/// (and border) color equal to the window background, so no OS-accent bar shows.
/// Caption/border color needs Windows 11; on older builds the calls fail harmlessly
/// and the dark-mode flag alone still gives a dark bar.
/// </summary>
public static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Applies the dark chrome now if the window has a handle, else as soon as it gets one.</summary>
    public static void Apply(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            ApplyCore(window);
        else
            window.SourceInitialized += (_, _) => ApplyCore(window);
    }

    private static void ApplyCore(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        Set(hwnd, DWMWA_CAPTION_COLOR, ColorRef("BgColor"));
        Set(hwnd, DWMWA_BORDER_COLOR, ColorRef("BorderColor"));
        Set(hwnd, DWMWA_TEXT_COLOR, ColorRef("TextPrimaryColor"));
    }

    private static void Set(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    /// <summary>A theme Color as a COLORREF (0x00BBGGRR).</summary>
    private static int ColorRef(string colorKey)
    {
        var c = (Color)Application.Current.Resources[colorKey];
        return c.R | (c.G << 8) | (c.B << 16);
    }
}
