namespace USBControl.Core;

public interface ITopologyService
{
    /// <summary>Builds a fresh snapshot of the USB tree (blocking call).</summary>
    TopologySnapshot Snapshot();

    /// <summary>Raised when the hardware layer detects a change (device arrival/removal).</summary>
    event Action? Changed;
}

public interface IDevicePowerService
{
    /// <summary>Enables or disables a device by instance id (requires elevation).</summary>
    (bool Ok, string? Error) SetEnabled(string instanceId, bool enable);

    bool IsDisabled(string instanceId);
}

public interface IProfileApplier
{
    ProfileApplyReport Apply(Profile profile, TopologySnapshot snapshot, IProgress<string>? progress = null);
}
