using System.Windows;
using System.Windows.Controls;

namespace USBControl.App.Services;

/// <summary>Tiny modal input dialog (avoids referencing Microsoft.VisualBasic).</summary>
public static class Prompt
{
    public static string? Show(Window owner, string title, string label, string? defaultValue = null)
    {
        var input = new System.Windows.Controls.TextBox
        {
            Text = defaultValue ?? "",
            Margin = new Thickness(0, 8, 0, 12),
        };

        var ok = new System.Windows.Controls.Button { Content = "OK", Width = 84, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", Width = 84, IsCancel = true };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label });
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = title,
            Owner = owner,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = panel,
        };

        ok.Click += (_, _) => win.DialogResult = true;
        input.Focus();
        input.SelectAll();

        return win.ShowDialog() == true ? input.Text : null;
    }
}
