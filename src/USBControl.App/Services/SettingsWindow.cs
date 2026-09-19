using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using USBControl.Core;

namespace USBControl.App.Services;

public static class SettingsWindow
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Show(AppController controller)
    {
        var s = controller.Settings;

        var chkAll = new CheckBox { Content = "Show every USB device (not just controllers/hubs)", IsChecked = s.ShowAllDevices, Margin = new Thickness(0, 4, 0, 0) };
        var chkEmpty = new CheckBox { Content = "Show empty ports", IsChecked = s.ShowEmptyPorts, Margin = new Thickness(0, 4, 0, 0) };
        var chkHidden = new CheckBox { Content = "Show ports you hid", IsChecked = s.ShowHiddenPorts, Margin = new Thickness(0, 4, 0, 0) };
        var chkAutostart = new CheckBox { Content = "Start with Windows (minimized)", IsChecked = IsAutostartEnabled(), Margin = new Thickness(0, 4, 0, 0) };
        var chkTray = new CheckBox { Content = "Minimize & close to the system tray (exit from the tray menu)", IsChecked = s.MinimizeToTray, Margin = new Thickness(0, 4, 0, 0) };

        var ok = new Button { Content = "Save", Width = 92, IsDefault = true, Margin = new Thickness(0, 16, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 92, IsCancel = true, Margin = new Thickness(0, 16, 0, 0) };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "USB Control runs elevated because enabling/disabling devices needs the same rights as Device Manager.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            Foreground = System.Windows.Media.Brushes.Gray,
        });
        panel.Children.Add(chkAll);
        panel.Children.Add(chkEmpty);
        panel.Children.Add(chkHidden);
        panel.Children.Add(chkAutostart);
        panel.Children.Add(chkTray);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = "USB Control — settings",
            // The main window may be hidden in the tray; an invisible owner breaks modal centering.
            Owner = Application.Current?.MainWindow is { IsVisible: true } m ? m : null,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = panel,
        };

        ok.Click += (_, _) =>
        {
            s.ShowAllDevices = chkAll.IsChecked == true;
            s.ShowEmptyPorts = chkEmpty.IsChecked == true;
            s.ShowHiddenPorts = chkHidden.IsChecked == true;
            s.MinimizeToTray = chkTray.IsChecked != false;
            SetAutostart(chkAutostart.IsChecked == true);
            s.StartWithWindows = chkAutostart.IsChecked == true;
            controller.Store.Save();
            controller.RefreshFromSettings();
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
