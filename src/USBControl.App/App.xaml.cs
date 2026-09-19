using System.IO;
using System.Windows;
using USBControl.App.Services;
using USBControl.Core;
using USBControl.Hardware;

namespace USBControl.App;

public partial class App : Application
{
    public static AppController Controller { get; private set; } = null!;
    public static TrayIconController? Tray { get; private set; }

    private static bool _shutdownRequested;

    /// <summary>True once the user asked for a real exit (tray menu) — windows must not cancel closing.</summary>
    public static bool IsShutdownRequested => _shutdownRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var log = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "USBControl", "crash.log");
                File.AppendAllText(log,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {args.Exception.GetType().FullName}: {args.Exception.Message}\n" +
                    args.Exception.StackTrace + "\n---\n");
            }
            catch
            {
                // never let logging itself take the app down
            }

            MessageBox.Show(args.Exception.Message, "USB Control — unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var store = new AppStore();
        store.Load();
        Accents.Apply(store.Data.Settings.Accent);
        Controller = new AppController(store, new UsbTopologyService(), new DevicePowerService());

        var win = new MainWindow();
        MainWindow = win;
        win.Show();

        var vm = (MainWindowViewModel)win.DataContext;
        Tray = new TrayIconController(Controller, vm);

        if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            win.HideToTray();
    }

    public static void RequestShutdown()
    {
        _shutdownRequested = true;
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray?.Dispose();
        (Controller as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
