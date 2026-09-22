using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace USBControl.App.Services;

/// <summary>
/// Borderless brand splash shown while the main window and topology come up (in
/// particular, it covers the UAC prompt / elevated relaunch and first-run JIT cost).
/// Built in code like the app's other auxiliary windows (see Prompt.cs), not XAML.
/// </summary>
public static class SplashWindow
{
    public static Window Create()
    {
        var res = Application.Current.Resources;

        var mark = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/porthole-mark.png")),
            Width = 40,
            Height = 40,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var title = new TextBlock
        {
            Text = "PORTHOLE",
            Style = (Style)res["AppTitle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, (double)res["Space16"], 0, 0),
        };

        var status = new TextBlock
        {
            Text = "Starting…",
            Foreground = (Brush)res["TextMutedBrush"],
            FontFamily = (FontFamily)res["FontMono"],
            FontSize = (double)res["FontSizeChip"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, (double)res["Space8"], 0, 0),
        };

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(mark);
        panel.Children.Add(title);
        panel.Children.Add(status);

        var border = new Border
        {
            BorderBrush = (Brush)res["BorderBrush"],
            BorderThickness = new Thickness(1),
            Background = (Brush)res["BgBrush"],
            Padding = new Thickness((double)res["Space32"]),
            Child = panel,
        };

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = border,
        };

        DarkTitleBar.Apply(win);
        return win;
    }
}
