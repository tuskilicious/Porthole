using System.IO;
using System.Reflection;
using System.Windows;
using USBControl.Core;

namespace USBControl.App.Services;

/// <summary>
/// Demo mode (--demo): builds a realistic simulated USB rig from USBControl.Testing's
/// FakeUsbBus and serves it to the app instead of the real hardware layer. No device
/// access, no store writes to the real %APPDATA% profile, and no elevation needed.
///
/// The Testing project references the App project (its TestingRig drives the real
/// AppController), so the App cannot take a compile-time ProjectReference on Testing —
/// the assembly is located next to the EXE and loaded via reflection instead.
/// The publish profile ships USBControl.Testing.dll alongside the app for exactly this.
/// </summary>
public static class DemoMode
{
    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));

    /// <summary>Value of "--screenshot &lt;file.png&gt;" (demo only), or null.</summary>
    public static string? ScreenshotPath(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i].Equals("--screenshot", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    /// <summary>
    /// Verification aid for "--demo --screenshot out.png": once the rig has settled, renders the
    /// window to out.png, then selects the Xbox controller, opens the editor's Details and renders
    /// out-selected.png, then out-panel.png (panel layout, Violet accent) and out-settings.png, and exits. Rendered in-process because GPU-drawn WPF content is invisible
    /// to external window capture.
    /// </summary>
    public static async Task CaptureAndExitAsync(MainWindow window, MainWindowViewModel vm, AppController controller, string path)
    {
        await Task.Delay(3500);
        Render(window, path);

        // Editor open with nothing selected: its empty state.
        vm.IsEditorOpen = true;
        await Task.Delay(600);
        Render(window, Path.ChangeExtension(path, null) + "-editor-empty.png");

        vm.SelectedPort = controller.PanelPorts.FirstOrDefault(p =>
            p.Device?.HardwareId.Contains("PID_0B12", StringComparison.OrdinalIgnoreCase) == true);
        window.UpdateLayout();
        if (FindExpander(window) is { } details)
            details.IsExpanded = true;
        await Task.Delay(2000);
        var stem = Path.ChangeExtension(path, null);
        Render(window, stem + "-selected.png");

        // A wide window: tiles should add columns and still fill the row.
        window.Width = 1900;
        window.UpdateLayout();
        await Task.Delay(1200);
        Render(window, stem + "-wide.png");
        window.Width = 1240;

        // Panel-layout mode under another accent palette.
        Accents.Apply("Violet");
        controller.IsPanelMode = true;
        await Task.Delay(1500);
        Render(window, stem + "-panel.png");

        // No devices at all.
        controller.IsPanelMode = false;
        controller.Hubs.Clear();
        await Task.Delay(600);
        Render(window, stem + "-nodevices.png");

        // Settings dialog (modal, so it opens from a queued dispatcher call).
        controller.IsPanelMode = false;
        _ = window.Dispatcher.BeginInvoke(() => SettingsWindow.Show(controller));
        await Task.Delay(1500);
        if (Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w != window && w.IsVisible) is { } dialog)
        {
            Render(dialog, stem + "-settings.png");
            dialog.Close();
        }

        Application.Current.Shutdown();
    }

    private static System.Windows.Controls.Expander? FindExpander(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Expander e)
                return e;
            if (FindExpander(child) is { } found)
                return found;
        }
        return null;
    }

    private static void Render(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(content);
        var size = new Size(content.ActualWidth, content.ActualHeight);

        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((System.Windows.Media.Brush)Application.Current.Resources["BgBrush"], null, new Rect(size));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(content), null, new Rect(size));
        }

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(size.Width * dpi.DpiScaleX), (int)(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>Builds the demo topology/power pair, or null if the Testing assembly is missing.</summary>
    public static (ITopologyService Topology, IDevicePowerService Power)? TryCreate()
    {
        var bus = CreateBus();
        if (bus is null)
            return null;

        var topology = (ITopologyService)Activator.CreateInstance(
            TestingType("FakeTopologyService")!, bus)!;
        var power = (IDevicePowerService)Activator.CreateInstance(
            TestingType("FakeDevicePowerService")!, bus)!;

        // The fake only models connected/disabled/hub states; stamp the headset's port
        // as Problem on every snapshot so the demo shows the red Error chip too.
        topology = new ProblemStampingTopology(topology, "VID_046D&PID_0A03");
        return (topology, power);
    }

    private static Type? TestingType(string name)
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "USBControl.Testing");
        return asm?.GetType($"USBControl.Testing.{name}", throwOnError: true);
    }

    /// <summary>Decorates a topology service, marking one device's port as Problem (demo error state).</summary>
    private sealed class ProblemStampingTopology : ITopologyService
    {
        private readonly ITopologyService _inner;
        private readonly string _identityPrefix;

        public event Action? Changed;

        public ProblemStampingTopology(ITopologyService inner, string identityPrefix)
        {
            _inner = inner;
            _identityPrefix = identityPrefix;
            _inner.Changed += () => Changed?.Invoke();
        }

        public TopologySnapshot Snapshot()
        {
            var snap = _inner.Snapshot();
            foreach (var port in snap.AllPorts)
            {
                if (port.Device is { } d
                    && d.Identity.StartsWith(_identityPrefix, StringComparison.OrdinalIgnoreCase)
                    && port.State == PortState.Connected)
                {
                    port.State = PortState.Problem;
                    d.ConnectionStatus = 5; // "not enough power" — exercises the error tooltip
                }
            }
            return snap;
        }
    }

    /// <summary>HardwareId fragment of the demo device that is shown permanently busy (HOTAS stick).</summary>
    public const string BusySample = "PID_00C1";

    /// <summary>
    /// Once the first snapshot has landed, gives the Xbox controller a generated device
    /// photo so the demo also shows the photo variant of the icon well.
    /// </summary>
    public static void SeedPhotoWhenReady(AppController controller)
    {
        System.ComponentModel.PropertyChangedEventHandler? handler = null;
        handler = (_, e) =>
        {
            if (e.PropertyName != nameof(AppController.Hubs))
                return;
            var port = controller.PanelPorts.FirstOrDefault(p =>
                p.Device?.HardwareId.Contains("PID_0B12", StringComparison.OrdinalIgnoreCase) == true);
            if (port is null)
                return;

            controller.PropertyChanged -= handler;
            controller.SetDevicePhoto(port.Entry, RenderDemoPhoto());
        };
        controller.PropertyChanged += handler;
    }

    /// <summary>A 256px accent-tinted picture of a gamepad, written to the temp folder.</summary>
    private static string RenderDemoPhoto()
    {
        var path = Path.Combine(Path.GetTempPath(), "USBControl-demo-photo.png");
        var res = Application.Current.Resources;
        var accent = (System.Windows.Media.Color)res["AccentColor"];
        var bg = (System.Windows.Media.Color)res["RaisedColor"];

        var back = new System.Windows.Media.LinearGradientBrush(bg, accent, 45);
        var pen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)res["TextPrimaryColor"]), 7)
        {
            StartLineCap = System.Windows.Media.PenLineCap.Round,
            EndLineCap = System.Windows.Media.PenLineCap.Round,
            LineJoin = System.Windows.Media.PenLineJoin.Round,
        };

        var shape = ((System.Windows.Media.Geometry)res["IconController"]).Clone();
        shape.Transform = new System.Windows.Media.MatrixTransform(
            new System.Windows.Media.Matrix(9, 0, 0, 9, 20, 30));

        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(back, null, new Rect(0, 0, 256, 256));
            dc.DrawGeometry(null, pen, shape);
        }

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(256, 256, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(path))
            encoder.Save(fs);
        return path;
    }

    private static object? CreateBus()
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "USBControl.Testing.dll");
        if (!File.Exists(dllPath))
        {
            MessageBox.Show(
                "Demo mode needs USBControl.Testing.dll next to Porthole.exe (it ships with the published build).\n\nExpected at: " + dllPath,
                "Porthole — demo mode", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var testing = Assembly.LoadFrom(dllPath);
        var busType = testing.GetType("USBControl.Testing.FakeUsbBus", throwOnError: true)!;

        // FakeUsbBus's ctor takes a params tuple array, and Activator.CreateInstance does
        // NOT expand params — invoke the constructor explicitly with a properly typed array.
        var ctor = busType.GetConstructors().Single(c => c.GetParameters().Length == 1);
        var hubArray = Array.CreateInstance(
            ctor.GetParameters()[0].ParameterType.GetElementType()!, 2);
        hubArray.SetValue(("Rear USB 3.2 Gen2", 6), 0);
        hubArray.SetValue(("Front panel", 6), 1);
        var bus = ctor.Invoke(new[] { hubArray })!;

        // FakeDeviceSpec initializers via reflection on the record's init-only props.
        var specType = testing.GetType("USBControl.Testing.FakeDeviceSpec", throwOnError: true)!;
        var hidFactory = specType.GetMethod("Hid", new[] { typeof(string), typeof(string), typeof(string), typeof(string) })!;

        object Spec(string vid, string pid, string product, string? serial = null,
            string className = "HIDClass", string[]? children = null, bool isHub = false, byte speed = 2)
        {
            // Start from the factory (sets Vid/Pid/Product/Children=[HID]) then override.
            object spec = hidFactory.Invoke(null, new object?[] { vid, pid, product, serial })!;
            void Set(string prop, object? value) =>
                specType.GetProperty(prop)!.SetValue(spec, value);
            Set("Vid", vid);
            Set("Pid", pid);
            Set("Product", product);
            Set("ClassName", className);
            if (serial is not null) Set("Serial", serial);
            if (children is not null) Set("Children", children);
            if (isHub) Set("IsHub", true);
            if (speed != 2) Set("Speed", speed);
            return spec;
        }

        object Plug(object spec, string port) =>
            bus.GetType().GetMethod("Plug", new[] { specType, typeof(string) })!
                .Invoke(bus, new[] { spec, port })!;

        void Disable(object instanceId) =>
            bus.GetType().GetMethod("Disable", new[] { typeof(string) })!
                .Invoke(bus, new object[] { (string)instanceId });

        // ---- Rear hub: the main battle station ----------------------------------
        // 1  Xbox controller with a persistent serial (renames/profiles re-attach);
        //    SeedPhoto gives it a device photo once the first snapshot has landed
        Plug(Spec("045E", "0B12", "Xbox One Controller", serial: "XBOXSERIAL01",
            children: new[] { "XUSB", "HID" }), "HUB01#1");
        // 2  fightstick, disabled on purpose (dimmed tile, gray switch, Disabled chip)
        Disable(Plug(Spec("0F0D", "0092", "Leverless Fightstick", serial: "LEVERLESS01",
            children: new[] { "HID" }), "HUB01#2"));
        // 3  keyboard (HID)
        Plug(Spec("046D", "C534", "G512 RGB Mechanical Keyboard", children: new[] { "HID" }), "HUB01#3");
        // 4  headset in the error state (red Error chip)
        Plug(Spec("046D", "0A03", "G435 Wireless Headset", className: "MEDIA",
            children: new[] { "HID" }), "HUB01#4");
        // 5  webcam
        Plug(Spec("046D", "085B", "C922 Pro Webcam", className: "Camera",
            children: new[] { "HID" }), "HUB01#5");
        // 6  storage
        Plug(Spec("0781", "5581", "SanDisk Ultra Flair", className: "DiskDrive",
            children: Array.Empty<string>()), "HUB01#6");

        // ---- Front hub: everything else ----------------------------------------
        // 1  a second controller (serial-less — moves would re-enumerate, like real hardware)
        Plug(Spec("054C", "05C4", "Wireless Controller", children: new[] { "HID" }), "HUB02#1");
        // 2  mouse
        Plug(Spec("046D", "C08B", "G502 HERO Gaming Mouse", children: new[] { "HID" }), "HUB02#2");
        // 3  hotas stick
        Plug(Spec("0F0D", "00C1", "HOTAS Stick", serial: "HOTAS001",
            children: new[] { "HID" }), "HUB02#3");
        // 4  unnamed device: exercises the "Unknown device (VID:PID)" fallback
        Plug(Spec("1A34", "F5B2", "", className: "USB",
            children: Array.Empty<string>()), "HUB02#4");
        // 5  downstream hub: gets the Hub chip and no switch
        Plug(Spec("05E3", "0610", "USB2.0 Hub", className: "USB",
            children: Array.Empty<string>(), isHub: true), "HUB02#5");
        // 6  empty (dashed outline)
        return bus;
    }
}
