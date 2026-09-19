using USBControl.Core;
using Xunit;

namespace USBControl.Tests;

public class ProfileEngineTests
{
    private static TopologySnapshot Snapshot()
    {
        var hub = new HubGroup { HubKey = "HUB1", DisplayName = "Test hub" };
        hub.Ports.Add(new PortEntry
        {
            HubKey = "HUB1", PortNumber = 1, State = PortState.Connected,
            Device = new UsbDeviceInfo { InstanceId = "USB\\VID_045E&PID_0B12\\A1", Identity = "VID_045E&PID_0B12", DisplayName = "Pad 1" },
        });
        hub.Ports.Add(new PortEntry
        {
            HubKey = "HUB1", PortNumber = 2, State = PortState.Connected,
            Device = new UsbDeviceInfo { InstanceId = "USB\\VID_0F0D&PID_00C1\\B2", Identity = "VID_0F0D&PID_00C1", DisplayName = "Stick" },
        });
        hub.Ports.Add(new PortEntry
        {
            HubKey = "HUB1", PortNumber = 3, State = PortState.Connected,
            Device = new UsbDeviceInfo { InstanceId = "USB\\VID_054C&PID_05C4\\C3", Identity = "VID_054C&PID_05C4", DisplayName = "DS4" },
        });
        return new TopologySnapshot { Hubs = { hub } };
    }

    [Fact]
    public void BuildOps_Enables_Wanted_In_Profile_Order_First()
    {
        var profile = new Profile
        {
            Name = "Co-op",
            Devices =
            {
                new ProfileEntry { Identity = "VID_054C&PID_05C4", InstanceId = "USB\\VID_054C&PID_05C4\\C3", Name = "DS4", Enabled = true, Order = 0 },
                new ProfileEntry { Identity = "VID_045E&PID_0B12", InstanceId = "USB\\VID_045E&PID_0B12\\A1", Name = "Pad 1", Enabled = true, Order = 1 },
            },
        };

        var snap = Snapshot();
        // Pad + DS4 are currently disabled; the stick (not in the profile) is connected.
        snap.AllPorts.First(p => p.Device!.InstanceId == "USB\\VID_045E&PID_0B12\\A1").State = PortState.Disabled;
        snap.AllPorts.First(p => p.Device!.InstanceId == "USB\\VID_054C&PID_05C4\\C3").State = PortState.Disabled;

        var ops = ProfileEngine.BuildOps(snap, profile);

        var enables = ops.Where(o => o.Enable).ToList();
        Assert.Equal(2, enables.Count);
        Assert.Equal("USB\\VID_054C&PID_05C4\\C3", enables[0].InstanceId); // profile order wins
        Assert.Equal("USB\\VID_045E&PID_0B12\\A1", enables[1].InstanceId);

        // The stick is not in the profile → still gets disabled.
        var disable = Assert.Single(ops.Where(o => !o.Enable));
        Assert.Equal("USB\\VID_0F0D&PID_00C1\\B2", disable.InstanceId);
    }

    [Fact]
    public void BuildOps_Disables_Everything_Else()
    {
        var profile = new Profile { Name = "Solo" };
        profile.Devices.Add(new ProfileEntry { Identity = "VID_045E&PID_0B12", InstanceId = "USB\\VID_045E&PID_0B12\\A1", Name = "Pad 1", Enabled = true });

        var ops = ProfileEngine.BuildOps(Snapshot(), profile);
        Assert.Equal(2, ops.Count(o => !o.Enable));
    }

    [Fact]
    public void Apply_Reports_Errors_Per_Device()
    {
        var power = new FakePower { FailOn = "USB\\VID_054C&PID_05C4\\C3" };
        var engine = new ProfileEngine(power);
        var profile = new Profile { Name = "X" };
        profile.Devices.Add(new ProfileEntry { Identity = "VID_054C&PID_05C4", InstanceId = "USB\\VID_054C&PID_05C4\\C3", Enabled = true });

        var snap = Snapshot();
        snap.AllPorts.First(p => p.Device!.InstanceId == "USB\\VID_054C&PID_05C4\\C3").State = PortState.Disabled;

        var report = engine.Apply(profile, snap, null);

        Assert.False(report.Success);
        Assert.Single(report.Errors);
        Assert.Contains("DS4", report.Errors[0]);
    }

    private sealed class FakePower : IDevicePowerService
    {
        public string? FailOn { get; set; }
        public List<(string Id, bool Enable)> Calls { get; } = new();

        public (bool Ok, string? Error) SetEnabled(string instanceId, bool enable)
        {
            Calls.Add((instanceId, enable));
            return instanceId == FailOn ? (false, "test failure") : (true, null);
        }

        public bool IsDisabled(string instanceId) => false;
    }
}
