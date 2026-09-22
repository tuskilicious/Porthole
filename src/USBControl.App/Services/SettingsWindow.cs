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

        double FontSizeMicro() => Num("FontSizeMicro");
        Thickness Gap(double top = 0, double bottom = 0) => new(0, top, 0, bottom);
        double S4() => Num("Space4");
        double S8() => Num("Space8");
        double S24() => Num("Space24");
        double FontSizeMeta() => Num("FontSizeMeta");
        double SwatchSize() => Num("SettingsSwatchSize");
        double DialogWidth() => Num("SettingsDialogWidth");
        Thickness SettingsPadding() => (Thickness)Application.Current.Resources["SettingsPadding"];

        // A selected swatch's ring needs to track "TextBrush" live (SetResourceReference, the C#
        // equivalent of {DynamicResource}) rather than a one-time snapshot — otherwise clicking
        // the *other* row's swatch (theme vs. accent) while this dialog is open leaves the ring
        // painted in the old theme's text color even though the rest of the app just repainted.
        void MarkSelected(Control c, bool selected)
        {
            if (selected)
                c.SetResourceReference(Control.BorderBrushProperty, "TextBrush");
            else
                c.BorderBrush = Brushes.Transparent;
        }

        TextBlock SectionLabel(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = (FontFamily)Application.Current.Resources["FontHeading"],
                FontSize = FontSizeMicro(),
                Margin = Gap(S24(), S4()),
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            return tb;
        }

        var chkAll = new CheckBox { Content = "Show every USB device (not just controllers/hubs)", IsChecked = s.ShowAllDevices, Margin = Gap(S8()) };
        var chkEmpty = new CheckBox { Content = "Show empty ports", IsChecked = s.ShowEmptyPorts, Margin = Gap(S8()) };
        var chkHidden = new CheckBox { Content = "Show ports you hid", IsChecked = s.ShowHiddenPorts, Margin = Gap(S8()) };
        var chkAutostart = new CheckBox { Content = "Start with Windows (minimized)", IsChecked = IsAutostartEnabled(), Margin = Gap(S8()) };
        var chkTray = new CheckBox { Content = "Minimize & close to the system tray (exit from the tray menu)", IsChecked = s.MinimizeToTray, Margin = Gap(S8()) };
        foreach (var chk in new[] { chkAll, chkEmpty, chkHidden, chkAutostart, chkTray })
            chk.SetResourceReference(Control.ForegroundProperty, "TextBrush");

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
                Background = new SolidColorBrush(p.Base),
                Style = (Style)Application.Current.Resources["SwatchButton"],
            };
            MarkSelected(swatch, p.Name.Equals(pendingAccent, StringComparison.OrdinalIgnoreCase));
            swatch.Click += (_, _) =>
            {
                pendingAccent = p.Name;
                Accents.Apply(p.Name); // instant preview across the whole app
                foreach (Button other in swatchRow.Children)
                    MarkSelected(other, ReferenceEquals(other.Tag, p));
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
            };
            MarkSelected(swatch, tm.Name.Equals(pendingTheme, StringComparison.OrdinalIgnoreCase));
            swatch.Click += (_, _) =>
            {
                pendingTheme = tm.Name;
                ThemeMode.Apply(tm.Name); // instant preview across the whole app
                controller.RefreshFromSettings(); // rebuilds tiles so peripheral icons pick up the new variant
                if (Application.Current?.MainWindow is { } main)
                    DarkTitleBar.Apply(main);
                foreach (Button other in themeRow.Children)
                    MarkSelected(other, ReferenceEquals(other.Tag, tm));
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
        var warning = new TextBlock
        {
            Text = "Porthole runs elevated because enabling/disabling devices needs the same rights as Device Manager.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = FontSizeMeta(),
        };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        panel.Children.Add(warning);

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
            Content = panel,
        };
        win.SetResourceReference(Window.BackgroundProperty, "BgBrush");

        DarkTitleBar.Apply(win);

        ok.Click += (_, _) =>
        {
            s.ShowAllDevices = chkAll.IsChecked == true;
            s.ShowEmptyPorts = chkEmpty.IsChecked == true;
            s.ShowHiddenPorts = chkHidden.IsChecked == true;
            s.MinimizeToTray = chkTray.IsChecked != false;
            s.Accent = pendingAccent;
            s.Theme = pendingTheme;
            s.StartWithWindows = SetAutostart(chkAutostart.IsChecked == true);
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

    /// <summary>Returns whether autostart actually ended up in the requested state, so the caller
    /// doesn't persist "on" when there was no process path to write (leaving Settings and the
    /// registry disagreeing from then on).</summary>
    private static bool SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (!enable)
        {
            key.DeleteValue("USBControl", throwOnMissingValue: false);
            return true;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;
        key.SetValue("USBControl", $"\"{exe}\" --minimized");
        return true;
    }
}
