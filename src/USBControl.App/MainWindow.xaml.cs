using System.ComponentModel;
using System.Windows;
using USBControl.App.Services;

namespace USBControl.App;

public partial class MainWindow : Window
{
    private static bool _balloonShownOnce;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        var vm = new MainWindowViewModel(App.Controller)
        {
            ProfileNamePrompt = name => Prompt.Show(this, name, "Profile name", $"My setup {DateTime.Now:yyyy-MM-dd HH:mm}"),
        };
        DataContext = vm;

        StateChanged += (_, _) =>
        {
            // Minimize lands in the tray instead of the taskbar (user preference).
            if (WindowState == WindowState.Minimized && App.Controller.Settings.MinimizeToTray)
                HideToTray();
        };

        // X button (and any other close path) hides to the tray; a real exit only
        // happens via the tray menu, which sets IsShutdownRequested first.
        Closing += (_, e) =>
        {
            if (!App.Controller.Settings.MinimizeToTray || App.IsShutdownRequested)
                return;
            e.Cancel = true;
            HideToTray();
        };

        Closed += (_, _) => App.Tray?.Dispose();
    }

    /// <summary>Hides the window and keeps the app alive in the tray.</summary>
    public void HideToTray()
    {
        Hide();
        if (!_balloonShownOnce)
        {
            _balloonShownOnce = true;
            App.Tray?.ShowBalloon("Porthole still runs here",
                "Devices stay enabled/disabled as configured. Click the tray icon to reopen.");
        }
    }
}
