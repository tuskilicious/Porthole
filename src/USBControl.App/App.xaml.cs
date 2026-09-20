using System.Diagnostics;
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

    /// <summary>True when started with --demo: a simulated rig serves the UI, no real device access.</summary>
    public static bool IsDemoMode { get; private set; }

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

        IsDemoMode = DemoMode.IsRequested(e.Args);

        // WPF binding failures are silent (no crash, just blank UI), so capture them:
        // always in demo mode (it's the diagnostic sandbox), otherwise behind the same
        // %APPDATA%\USBControl\debug.flag opt-in Hardware.Diag uses.
        if (IsDemoMode || File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "USBControl", "debug.flag")))
            EnableBindingTrace();

        if (IsDemoMode)
        {
            // Demo mode never touches real devices, so it must NOT require elevation.
        }
        else if (!EnsureElevated())
        {
            // A child process was started elevated (or the user cancelled UAC); this
            // instance exits either way. The elevated child re-runs OnStartup.
            Shutdown();
            return;
        }

        if (!IsDemoMode && !IsElevated())
        {
            // Relaunched with the marker but still not admin (e.g. UAC declined twice) —
            // say why instead of showing a mystery empty device list.
            MessageBox.Show(
                "USB Control needs administrator rights to enable/disable devices (the same rights Device Manager uses).",
                "USB Control — elevation required", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        var store = IsDemoMode
            ? new AppStore(Path.Combine(Path.GetTempPath(), "USBControl-demo"))
            : new AppStore();
        store.Load();
        Accents.Apply(store.Data.Settings.Accent);

        ITopologyService topology;
        IDevicePowerService power;
        if (IsDemoMode)
        {
            var demo = DemoMode.TryCreate();
            if (demo is null)
            {
                Shutdown();
                return;
            }
            (topology, power) = demo.Value;
        }
        else
        {
            topology = new UsbTopologyService();
            power = new DevicePowerService();
        }

        Controller = new AppController(store, topology, power)
        {
            Confirm = ConfirmDialog,
        };
        if (IsDemoMode)
        {
            // Demo closes like a normal window instead of hiding in the tray —
            // it's a sandboxed run, there is nothing to keep alive.
            Controller.Settings.MinimizeToTray = false;
            // The demo rig is the product tour: empty ports and non-game devices
            // (webcam, storage, unnamed) are part of the story.
            Controller.Settings.ShowEmptyPorts = true;
            Controller.Settings.ShowAllDevices = true;
            PortViewModel.DemoBusyMatch = DemoMode.BusySample;
            DemoMode.SeedPhotoWhenReady(Controller);
        }

        var win = new MainWindow();
        if (IsDemoMode)
            win.Title = "USB Control — demo (simulated devices)";
        MainWindow = win;
        win.Show();

        var vm = (MainWindowViewModel)win.DataContext;
        Tray = new TrayIconController(Controller, vm);

        if (IsDemoMode && DemoMode.ScreenshotPath(e.Args) is { } shot)
            _ = DemoMode.CaptureAndExitAsync(win, vm, Controller, shot);

        if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            win.HideToTray();
    }

    /// <summary>Yes/No warning dialog (defaults to No), owned by the main window when it is showing.</summary>
    private static bool ConfirmDialog(string title, string message)
    {
        var owner = Current.MainWindow is { IsVisible: true } w ? w : null;
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
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

    // ---------------- WPF binding-error capture ----------------

    /// <summary>
    /// Routes WPF DataBinding trace output to %APPDATA%\USBControl\binding-errors.log.
    /// A binding that fails (null DataContext, wrong path) never crashes — it just
    /// renders blank, so without this the only symptom is an empty tile.
    /// </summary>
    private static void EnableBindingTrace()
    {
        try
        {
            PresentationTraceSources.Refresh();
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "USBControl");
            Directory.CreateDirectory(dir);
            PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorListener(
                Path.Combine(dir, "binding-errors.log")));
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        }
        catch
        {
            // tracing is best-effort; never block startup on it
        }
    }

    private sealed class BindingErrorListener(string path) : TraceListener
    {
        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            try
            {
                File.AppendAllText(path,
                    $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // never let logging itself take the app down
            }
        }
    }

    // ---------------- elevation ----------------

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relaunches itself elevated via the ShellExecute "runas" verb (the manifest now
    /// starts asInvoker so --demo can run unelevated). Returns true when already
    /// elevated; otherwise starts the elevated child and returns false so this
    /// instance exits. The child inherits the command line plus an --elevating marker,
    /// which guarantees we never loop if elevation somehow didn't stick.
    /// </summary>
    private static bool EnsureElevated()
    {
        if (IsElevated())
            return true;

        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        if (args.Contains("--elevating", StringComparer.OrdinalIgnoreCase))
            return false;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(" ", args.Select(EscapeArg).Append("--elevating")),
        };
        try
        {
            Process.Start(psi);
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // user cancelled the UAC prompt
        }
    }

    private static string EscapeArg(string arg) =>
        arg.Contains(' ') ? '"' + arg + '"' : arg;
}
