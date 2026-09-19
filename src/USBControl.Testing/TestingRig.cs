using USBControl.App.Services;
using USBControl.Core;

namespace USBControl.Testing;

/// <summary>Standard simulated gaming rig: two hubs, four controllers.</summary>
public static class FakeRigs
{
    public static FakeDeviceSpec XboxPad(string? serial = null) =>
        new() { Vid = "045E", Pid = "0B12", Product = "Xbox One Controller", Serial = serial, Children = new[] { "XUSB", "HID" } };

    public static FakeDeviceSpec Ds4(string? serial = null) =>
        new() { Vid = "054C", Pid = "05C4", Product = "Wireless Controller", Serial = serial, Children = new[] { "HID" } };

    public static FakeDeviceSpec FlightStick(string? serial = null) =>
        new() { Vid = "0F0D", Pid = "00C1", Product = "HOTAS Stick", Serial = serial, Children = new[] { "HID" } };

    public static FakeDeviceSpec Leverless(string? serial = null) =>
        new() { Vid = "0F0D", Pid = "0092", Product = "Leverless Fightstick", Serial = serial, Children = new[] { "HID" } };

    /// <summary>Two hubs (4 + 4 ports) with a pad, a DS4, a stick and a leverless plugged in.</summary>
    public static FakeUsbBus GamingRig(out Dictionary<string, string> instanceIds)
    {
        var bus = new FakeUsbBus(("Rear USB 3.2", 4), ("Front panel", 4));
        instanceIds = new Dictionary<string, string>
        {
            ["pad1"] = bus.Plug(XboxPad("XBOXSERIAL1"), "HUB01#1"),
            ["ds4"] = bus.Plug(Ds4(), "HUB01#2"),
            ["stick"] = bus.Plug(FlightStick("HOTAS001"), "HUB02#1"),
            ["leverless"] = bus.Plug(Leverless(), "HUB02#2"),
        };
        return bus;
    }
}

/// <summary>
/// Complete in-memory harness: bus + fake services + temp-dir store + the real
/// AppController. Lets integration tests drive the actual application pipeline.
/// </summary>
public sealed class TestingRig : IDisposable
{
    public FakeUsbBus Bus { get; }
    public FakeTopologyService Topology { get; }
    public FakeDevicePowerService Power { get; }
    public AppStore Store { get; }
    public AppController Controller { get; }
    public string RootDir { get; }

    public TestingRig(FakeUsbBus? bus = null, string? rootDir = null)
    {
        Bus = bus ?? new FakeUsbBus(("Rear USB 3.2", 4), ("Front panel", 4));
        Topology = new FakeTopologyService(Bus);
        Power = new FakeDevicePowerService(Bus);

        RootDir = rootDir ?? Path.Combine(Path.GetTempPath(), "usbcontrol-rig-" + Guid.NewGuid().ToString("N"));
        Store = new AppStore(RootDir);
        Store.Load();

        // AppController is UI-aware but tolerates a null SynchronizationContext
        // (all RunOnUi calls execute inline), which is exactly what tests provide.
        Controller = new AppController(Store, Topology, Power);
    }

    /// <summary>Waits until the controller has fully processed the latest bus change.</summary>
    public async Task SettleAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Controller.IsBusy && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        if (Controller.IsBusy)
            throw new TimeoutException("controller did not settle within 5s");

        await Task.Delay(50); // let post-refresh store writes finish
    }

    public void Dispose()
    {
        Controller.Dispose();
        try { Directory.Delete(RootDir, recursive: true); } catch { /* best effort */ }
    }
}
