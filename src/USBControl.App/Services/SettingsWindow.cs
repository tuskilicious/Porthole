using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using USBControl.Core;

namespace USBControl.App.Services;

public static class SettingsWindow
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Show(AppController controller)
    {
        var s = controller.Settings;

        Brush Bg() => (Brush)Application.Current.Resources["BgBrush"];
        Brush Text() => (Brush)Application.Current.Resources["TextBrush"];
        Brush Dim() => (Brush)Application.Current.Resources["TextDimBrush"];

        TextBlock SectionLabel(string text) => new()
        {
            Text = text,
            FontSize = 10.5,
            Foreground = Dim(),
            Margin = new Thickness(0, 14, 0, 2),
        };

        var chkAll = new CheckBox { Content = "Show every USB device (not just controllers/hubs)", IsChecked = s.ShowAllDevices, Margin = new Thickness(0, 4, 0, 0), Foreground = Text() };
        var chkEmpty = new CheckBox { Content = "Show empty ports", IsChecked = s.ShowEmptyPorts, Margin = new Thickness(0, 4, 0, 0), Foreground = Text() };
        var chkHidden = new CheckBox { Content = "Show ports you hid", IsChecked = s.ShowHiddenPorts, Margin = new Thickness(0, 4, 0, 0), Foreground = Text() };
        var chkAutostart = new CheckBox { Content = "Start with Windows (minimized)", IsChecked = IsAutostartEnabled(), Margin = new Thickness(0, 4, 0, 0), Foreground = Text() };
        var chkTray = new CheckBox { Content = "Minimize & close to the system tray (exit from the tray menu)", IsChecked = s.MinimizeToTray, Margin = new Thickness(0, 4, 0, 0), Foreground = Text() };

        // --- accent swatches (live preview; persisted on Save like the checkboxes) ---
        var pendingAccent = s.Accent;
        var swatchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var palette in Accents.All)
        {
            var p = palette;
            var swatch = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = p.Name,
                BorderThickness = new Thickness(2),
                BorderBrush = p.Name.Equals(pendingAccent, StringComparison.OrdinalIgnoreCase) ? Text() : Brushes.Transparent,
                Background = new SolidColorBrush(p.Base),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            swatch.Click += (_, _) =>
            {
                pendingAccent = p.Name;
                Accents.Apply(p.Name); // instant preview across the whole app
                foreach (Button other in swatchRow.Children)
                    other.BorderBrush = ReferenceEquals(other.Tag, p) ? Text() : Brushes.Transparent;
                swatch.Tag = p;
            };
            swatch.Tag = p;
            swatchRow.Children.Add(swatch);
        }

        var ok = new Button { Content = "Save", Width = 92, IsDefault = true, Margin = new Thickness(0, 18, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 92, IsCancel = true, Margin = new Thickness(0, 18, 0, 0) };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "USB Control runs elevated because enabling/disabling devices needs the same rights as Device Manager.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 372,
            FontSize = 11.5,
            Foreground = Dim(),
        });

        panel.Children.Add(SectionLabel("VIEW"));
        panel.Children.Add(chkAll);
        panel.Children.Add(chkEmpty);
        panel.Children.Add(chkHidden);

        panel.Children.Add(SectionLabel("BEHAVIOR"));
        panel.Children.Add(chkAutostart);
        panel.Children.Add(chkTray);

        panel.Children.Add(SectionLabel("ACCENT"));
        panel.Children.Add(swatchRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = "USB Control — settings",
            // The main window may be hidden in the tray; an invisible owner breaks modal centering.
            Owner = Application.Current?.MainWindow is { IsVisible: true } m ? m : null,
            Width = 448,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Bg(),
            Content = panel,
        };

        ok.Click += (_, _) =>
        {
            s.ShowAllDevices = chkAll.IsChecked == true;
            s.ShowEmptyPorts = chkEmpty.IsChecked == true;
            s.ShowHiddenPorts = chkHidden.IsChecked == true;
            s.MinimizeToTray = chkTray.IsChecked != false;
            s.Accent = pendingAccent;
            SetAutostart(chkAutostart.IsChecked == true);
            s.StartWithWindows = chkAutostart.IsChecked == true;
            controller.Store.Save();
            controller.RefreshFromSettings();
            win.Close();
        };
        cancel.Click += (_, _) =>
        {
            Accents.Apply(s.Accent); // undo the live preview
            win.Close();
        };

        win.ShowDialog();
    }

    private static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue("USBControl") is string;
    }

    private static void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enable)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                key.SetValue("USBControl", $"\"{exe}\" --minimized");
        }
        else
        {
            key.DeleteValue("USBControl", throwOnMissingValue: false);
        }
    }
}
