using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using USBControl.Core;

namespace USBControl.App.Services;

public static class SettingsWindow
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static double Num(string key) => (double)Application.Current.Resources[key];

    public static void Show(AppController controller)
    {
        var s = controller.Settings;

        Brush Bg() => (Brush)Application.Current.Resources["BgBrush"];
        Brush Text() => (Brush)Application.Current.Resources["TextBrush"];
        Brush Dim() => (Brush)Application.Current.Resources["TextDimBrush"];
        double FontSizeMicro() => Num("FontSizeMicro");
        Thickness Gap(double top = 0, double bottom = 0) => new(0, top, 0, bottom);
        double S4() => Num("Space4");
        double S8() => Num("Space8");
        double S24() => Num("Space24");
        double FontSizeMeta() => Num("FontSizeMeta");
        double SwatchSize() => Num("SettingsSwatchSize");
        double DialogWidth() => Num("SettingsDialogWidth");
        Thickness SettingsPadding() => (Thickness)Application.Current.Resources["SettingsPadding"];

        TextBlock SectionLabel(string text) => new()
        {
            Text = text,
            FontFamily = (FontFamily)Application.Current.Resources["FontHeading"],
            FontSize = FontSizeMicro(),
            Foreground = Dim(),
            Margin = Gap(S24(), S4()),
        };

        var chkAll = new CheckBox { Content = "Show every USB device (not just controllers/hubs)", IsChecked = s.ShowAllDevices, Margin = Gap(S8()), Foreground = Text() };
        var chkEmpty = new CheckBox { Content = "Show empty ports", IsChecked = s.ShowEmptyPorts, Margin = Gap(S8()), Foreground = Text() };
        var chkHidden = new CheckBox { Content = "Show ports you hid", IsChecked = s.ShowHiddenPorts, Margin = Gap(S8()), Foreground = Text() };
        var chkAutostart = new CheckBox { Content = "Start with Windows (minimized)", IsChecked = IsAutostartEnabled(), Margin = Gap(S8()), Foreground = Text() };
        var chkTray = new CheckBox { Content = "Minimize & close to the system tray (exit from the tray menu)", IsChecked = s.MinimizeToTray, Margin = Gap(S8()), Foreground = Text() };

        // --- accent swatches (live preview; persisted on Save like the checkboxes) ---
        var pendingAccent = s.Accent;
        var swatchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = Gap(S8()) };
        foreach (var palette in Accents.All)
        {
            var p = palette;
            var swatch = new Button
            {
                Width = SwatchSize(),
                Height = SwatchSize(),
                Margin = new Thickness(0, 0, S8(), 0),
                ToolTip = p.Name,
                BorderThickness = new Thickness(2),
                BorderBrush = p.Name.Equals(pendingAccent, StringComparison.OrdinalIgnoreCase) ? Text() : Brushes.Transparent,
                Background = new SolidColorBrush(p.Base),
                Style = (Style)Application.Current.Resources["SwatchButton"],
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

        // --- theme (Dark/Light) — same instant-preview/Cancel-revert pattern as the accent ---
        var pendingTheme = s.Theme;
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = Gap(S8()) };
        foreach (var mode in ThemeMode.All)
        {
            var tm = mode;
            var swatch = new Button
            {
                Content = tm.Name,
                Width = 92,
                Height = SwatchSize(),
                Margin = new Thickness(0, 0, S8(), 0),
                BorderThickness = new Thickness(2),
                BorderBrush = tm.Name.Equals(pendingTheme, StringComparison.OrdinalIgnoreCase) ? Text() : Brushes.Transparent,
            };
            swatch.Click += (_, _) =>
            {
                pendingTheme = tm.Name;
                ThemeMode.Apply(tm.Name); // instant preview across the whole app
                controller.RefreshFromSettings(); // rebuilds tiles so peripheral icons pick up the new variant
                if (Application.Current?.MainWindow is { } main)
                    DarkTitleBar.Apply(main);
                foreach (Button other in themeRow.Children)
                    other.BorderBrush = ReferenceEquals(other.Tag, tm) ? Text() : Brushes.Transparent;
            };
            swatch.Tag = tm;
            themeRow.Children.Add(swatch);
        }

        var ok = new Button
        {
            Content = "Save", Width = 92, IsDefault = true, Margin = new Thickness(0, S24(), S8(), 0),
            Style = (Style)Application.Current.Resources["PrimaryButton"],
        };
        var cancel = new Button { Content = "Cancel", Width = 92, IsCancel = true, Margin = Gap(S24()) };

        var panel = new StackPanel { Margin = SettingsPadding() };
        panel.Children.Add(new TextBlock
        {
            Text = "Porthole runs elevated because enabling/disabling devices needs the same rights as Device Manager.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = FontSizeMeta(),
            Foreground = Dim(),
        });

        panel.Children.Add(SectionLabel("VIEW"));
        panel.Children.Add(chkAll);
        panel.Children.Add(chkEmpty);
        panel.Children.Add(chkHidden);

        panel.Children.Add(SectionLabel("BEHAVIOR"));
        panel.Children.Add(chkAutostart);
        panel.Children.Add(chkTray);

        panel.Children.Add(SectionLabel("THEME"));
        panel.Children.Add(themeRow);

        panel.Children.Add(SectionLabel("ACCENT"));
        panel.Children.Add(swatchRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = "Porthole — settings",
            // The main window may be hidden in the tray; an invisible owner breaks modal centering.
            Owner = Application.Current?.MainWindow is { IsVisible: true } m ? m : null,
            Width = DialogWidth(),
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Bg(),
            Content = panel,
        };

        DarkTitleBar.Apply(win);

        ok.Click += (_, _) =>
        {
            s.ShowAllDevices = chkAll.IsChecked == true;
            s.ShowEmptyPorts = chkEmpty.IsChecked == true;
            s.ShowHiddenPorts = chkHidden.IsChecked == true;
            s.MinimizeToTray = chkTray.IsChecked != false;
            s.Accent = pendingAccent;
            s.Theme = pendingTheme;
            SetAutostart(chkAutostart.IsChecked == true);
            s.StartWithWindows = chkAutostart.IsChecked == true;
            controller.Store.Save();
            controller.RefreshFromSettings();
            win.Close();
        };
        cancel.Click += (_, _) =>
        {
            Accents.Apply(s.Accent); // undo the live preview
            ThemeMode.Apply(s.Theme);
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
