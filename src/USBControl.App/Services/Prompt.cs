using System.Windows;
using System.Windows.Controls;

namespace USBControl.App.Services;

/// <summary>Tiny modal input dialog (avoids referencing Microsoft.VisualBasic).</summary>
public static class Prompt
{
    public static string? Show(Window owner, string title, string label, string? defaultValue = null)
    {
        var gap = (double)Application.Current.Resources["Space8"];
        var input = new System.Windows.Controls.TextBox
        {
            Text = defaultValue ?? "",
            Margin = new Thickness(0, gap, 0, (double)Application.Current.Resources["Space16"]),
        };

        var ok = new System.Windows.Controls.Button
        {
            Content = "OK", Width = 84, IsDefault = true, Margin = new Thickness(0, 0, gap, 0),
            Style = (Style)Application.Current.Resources["PrimaryButton"],
        };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", Width = 84, IsCancel = true };

        var panel = new StackPanel { Margin = new Thickness((double)Application.Current.Resources["Space24"]) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = label,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextDimBrush"],
        });
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = title,
            Owner = owner,
            Width = (double)Application.Current.Resources["PromptDialogWidth"],
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = panel,
        };

        DarkTitleBar.Apply(win);
        ok.Click += (_, _) => win.DialogResult = true;
        input.Focus();
        input.SelectAll();

        return win.ShowDialog() == true ? input.Text : null;
    }
}
