using USBControl.Core;

namespace USBControl.Testing;

/// <summary>
/// Simulates a USB topology: named hubs with numbered ports, pluggable devices,
/// hot-plug/unplug events, devices moving between ports, and Device-Manager-style
/// persistent enable/disable. Produces real TopologySnapshots so the app's merge,
/// matching, and profile logic can be exercised end-to-end without hardware.
/// </summary>
public sealed class FakeUsbBus
{
    private int _hubCounter;
    private int _locationCounter;
    private readonly Dictionary<string, Port> _ports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Plugged> _plugged = new(StringComparer.OrdinalIgnoreCase);    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase); // instance ids

    public event Action? Changed;

    public FakeUsbBus(params (string HubName, int PortCount)[] hubs)
    {
        foreach (var (name, count) in hubs)
            AddHub(name, count);
    }

    public void AddHub(string name, int portCount)
    {
        _hubCounter++;
        var key = $"HUB{_hubCounter:00}";
        for (var p = 1; p <= portCount; p++)
            _ports[$"{key}#{p}"] = new Port(key, p, name);
    }

    // ---------------- plugging ----------------

    /// <summary>Plugs a spec into a fake port ("HUB01#3"); returns the device instance id.</summary>
    public string Plug(FakeDeviceSpec spec, string portKey)
    {
        if (!_ports.TryGetValue(portKey, out var port))
            throw new ArgumentException($"unknown port '{portKey}'");

        var location = $"6&{_locationCounter++:x}&0&{port.PortNumber}";
        var instanceId = spec.Serial is null
            ? $"USB\\VID_{spec.Vid}&PID_{spec.Pid}\\{location}"
            : $"USB\\VID_{spec.Vid}&PID_{spec.Pid}\\{spec.Serial}";

        if (_plugged.ContainsKey(portKey))
            throw new InvalidOperationException($"port '{portKey}' already occupied");

        _plugged[portKey] = new Plugged(spec, instanceId, portKey);
        RaiseChanged();
        return instanceId;
    }

    /// <summary>Removes whatever is in the port.</summary>
    public bool Unplug(string portKey)
    {
        if (!_plugged.Remove(portKey, out _))
            return false;
        RaiseChanged();
        return true;
    }

    /// <summary>Physically moves a device to another port (new location tail, same identity).</summary>
    public string Move(string fromPort, string toPort)
    {
        if (!_plugged.TryGetValue(fromPort, out var dev))
            throw new InvalidOperationException($"nothing in '{fromPort}'");
        if (_plugged.ContainsKey(toPort))
            throw new InvalidOperationException($"'{toPort}' occupied");

        // Serial devices keep their identity across ports (serial travels with hardware);
        // serial-less ones get a fresh location tail, exactly like the real bus.
        var spec = dev.Spec;
        var newLocation = $"9&{_locationCounter++:x}&0&{ParsePortNumber(toPort)}";
        var newInstanceId = spec.Serial is null
            ? $"USB\\VID_{spec.Vid}&PID_{spec.Pid}\\{newLocation}"
            : dev.InstanceId;

        _plugged.Remove(fromPort);
        _plugged[toPort] = new Plugged(spec, newInstanceId, toPort);
        RaiseChanged();
        return newInstanceId;
    }

    // ---------------- power (Device Manager semantics) ----------------

    /// <summary>
    /// Disables a devnode; the state persists across unplug/replug, like CM_DISABLE_PERSISTENT.
    /// Disabling an already-disabled (existing) device succeeds as a no-op, like the real API.
    /// </summary>
    public bool Disable(string instanceId)
    {
        if (!_plugged.Any(kv => kv.Value.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (_disabled.Add(instanceId))
            RaiseChanged();
        return true;
    }

    /// <summary>Enabling an already-enabled (existing) device succeeds as a no-op, like CM_Enable.</summary>
    public bool Enable(string instanceId)
    {
        if (!_plugged.Any(kv => kv.Value.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (_disabled.Remove(instanceId))
            RaiseChanged();
        return true;
    }

    public bool IsDisabled(string instanceId) => _disabled.Contains(instanceId);

    /// <summary>
    /// Simulates "reboot": the fake's only state is the persistent disable set, which
    /// (correctly) survives, so this just notifies listeners like a re-enumeration would.
    /// </summary>
    public void Reboot() => RaiseChanged();

    // ---------------- snapshot ----------------

    /// <summary>Builds a snapshot exactly like the hardware layer would produce.</summary>
    public TopologySnapshot Snapshot()
    {
        var snapshot = new TopologySnapshot();
        foreach (var portGroup in _ports.Values.GroupBy(p => p.HubKey))
        {
            var first = portGroup.First();
            var hub = new HubGroup { HubKey = first.HubKey, DisplayName = first.HubName };
            foreach (var port in portGroup.OrderBy(p => p.PortNumber))
                hub.Ports.Add(DescribePort(port));
            snapshot.Hubs.Add(hub);
        }
        return snapshot;
    }

    private PortEntry DescribePort(Port port)
    {
        var entry = new PortEntry { HubKey = port.HubKey, PortNumber = port.PortNumber };

        if (!_plugged.TryGetValue(port.Key, out var plugged))
            return entry; // empty

        var spec = plugged.Spec;
        var disabled = _disabled.Contains(plugged.InstanceId);

        var device = new UsbDeviceInfo
        {
            InstanceId = plugged.InstanceId,
            Identity = DeviceIdentity.Build(plugged.InstanceId),
            DisplayName = spec.Product,
            Manufacturer = spec.Manufacturer,
            HardwareId = $"USB\\VID_{spec.Vid}&PID_{spec.Pid}",
            Serial = spec.Serial ?? "",
            ClassName = spec.IsHub ? "USB" : spec.ClassName,
            IsHub = spec.IsHub,
            Speed = spec.Speed,
            GameRelevant = spec.ClassName == "HIDClass"
                || spec.Children.Any(c => c is "HID" or "XUSB"),
        };

        // The XUSB/XINPUT functional child carries the recognizable name.
        if (spec.Children.Contains("XUSB"))
            device.ChildDisplayName = $"{spec.Product} (XINPUT)";

        entry.Device = device;
        entry.State = disabled ? PortState.Disabled
            : spec.IsHub ? PortState.Hub
            : PortState.Connected;
        return entry;
    }

    private static int ParsePortNumber(string portKey)
    {
        var hash = portKey.IndexOf('#');
        return hash >= 0 && int.TryParse(portKey[(hash + 1)..], out var n) ? n : 1;
    }

    private void RaiseChanged() => Changed?.Invoke();

    private sealed record Port(string HubKey, int PortNumber, string HubName)
    {
        public string Key => $"{HubKey}#{PortNumber}";
    }

    private sealed record Plugged(FakeDeviceSpec Spec, string InstanceId, string PortKey);
}
