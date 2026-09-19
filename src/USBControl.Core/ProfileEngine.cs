namespace USBControl.Core;

public sealed class ProfileEngine : IProfileApplier
{
    private readonly IDevicePowerService _power;

    public ProfileEngine(IDevicePowerService power)
    {
        _power = power;
    }

    /// <summary>
    /// Computes the operations to apply a profile: enable profile devices in profile order
    /// (enumeration order influences gamepad index), then disable every other connected device.
    /// </summary>
    public static List<ProfileOp> BuildOps(TopologySnapshot snapshot, Profile profile)
    {
        var targets = snapshot.AllPorts
            .Where(p => p.Device is { IsHub: false })
            .GroupBy(p => p.Device!.Identity, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var wanted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in profile.Devices)
            wanted[e.Identity] = e.Enabled;

        var ops = new List<ProfileOp>();

        // Enables first, in profile order (Order field — this drives gamepad enumeration order).
        // Devices already in the wanted state are skipped, so applying twice is a no-op.
        foreach (var entry in profile.Devices.Where(d => d.Enabled).OrderBy(d => d.Order))
        {
            var group = targets.FirstOrDefault(g => g.Key.Equals(entry.Identity, StringComparison.OrdinalIgnoreCase));
            if (group is null)
                continue;
            foreach (var port in group.Where(p => p.State == PortState.Disabled))
                ops.Add(new ProfileOp(port.Device!.InstanceId, true, Display(port)));
        }

        // Then disable everything the profile does not want (only if currently enabled).
        foreach (var group in targets)
        {
            var want = wanted.TryGetValue(group.Key, out var w) && w;
            if (want)
                continue;
            foreach (var port in group.Where(p => p.State != PortState.Disabled))
                ops.Add(new ProfileOp(port.Device!.InstanceId, false, Display(port)));
        }

        return ops;

        static string Display(PortEntry p) =>
            string.IsNullOrWhiteSpace(p.Device!.DisplayName) ? p.Device.InstanceId : p.Device.DisplayName;
    }

    /// <summary>Captures the current enabled/disabled state of every non-hub device as a profile.</summary>
    public static Profile Capture(TopologySnapshot snapshot, string name)
    {
        var profile = new Profile { Name = name };
        var order = 0;
        foreach (var port in snapshot.AllPorts.Where(p => p.Device is { IsHub: false }))
        {
            profile.Devices.Add(new ProfileEntry
            {
                Identity = port.Device!.Identity,
                InstanceId = port.Device.InstanceId,
                Name = port.Device.DisplayName,
                Enabled = port.State != PortState.Disabled,
                Order = order++,
            });
        }
        return profile;
    }

    public ProfileApplyReport Apply(Profile profile, TopologySnapshot snapshot, IProgress<string>? progress = null)
    {
        var ops = BuildOps(snapshot, profile);
        var errors = new List<string>();
        int enabled = 0, disabled = 0;

        foreach (var op in ops)
        {
            progress?.Report($"{(op.Enable ? "Enabling" : "Disabling")} {op.DisplayName}…");
            var (ok, error) = _power.SetEnabled(op.InstanceId, op.Enable);
            if (ok)
            {
                if (op.Enable) enabled++; else disabled++;
                // Give the bus a moment between enables so Windows assigns
                // controller indices in a deterministic order.
                if (op.Enable)
                    Thread.Sleep(250);
            }
            else
            {
                errors.Add($"{op.DisplayName}: {error}");
            }
        }

        return new ProfileApplyReport(enabled, disabled, errors);
    }
}
