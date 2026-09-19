using USBControl.Core;

namespace USBControl.Testing;

/// <summary>ITopologyService over a FakeUsbBus; raises Changed on bus events (debounce-free).</summary>
public sealed class FakeTopologyService : ITopologyService
{
    private readonly FakeUsbBus _bus;

    public FakeTopologyService(FakeUsbBus bus)
    {
        _bus = bus;
        _bus.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public TopologySnapshot Snapshot() => _bus.Snapshot();
}

/// <summary>
/// IDevicePowerService over a FakeUsbBus, with devcon-style per-call failure injection
/// and a full call log for assertions.
/// </summary>
public sealed class FakeDevicePowerService : IDevicePowerService
{
    private readonly FakeUsbBus _bus;
    private readonly HashSet<string> _failOn = new(StringComparer.OrdinalIgnoreCase);

    public FakeDevicePowerService(FakeUsbBus bus) => _bus = bus;

    public List<(string InstanceId, bool Enable)> Calls { get; } = new();

    /// <summary>Makes the next SetEnabled calls for this instance id fail (like a busy devnode).</summary>
    public void FailOn(string instanceId) => _failOn.Add(instanceId);

    public void ClearFailures() => _failOn.Clear();

    public (bool Ok, string? Error) SetEnabled(string instanceId, bool enable)
    {
        Calls.Add((instanceId, enable));

        if (_failOn.Contains(instanceId))
            return (false, "simulated failure (devnode busy)");

        var ok = enable ? _bus.Enable(instanceId) : _bus.Disable(instanceId);
        return ok ? (true, null) : (false, $"device node not found: {instanceId}");
    }

    public bool IsDisabled(string instanceId) => _bus.IsDisabled(instanceId);
}
