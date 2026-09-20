using Xunit;
using USBControl.App.Services;
using USBControl.Core;

namespace USBControl.Tests;

public class InputGuardTests
{
    private static PortEntry Port(int n, string name, string cls, PortState state = PortState.Connected) => new()
    {
        HubKey = "HUB01",
        PortNumber = n,
        State = state,
        Device = new UsbDeviceInfo
        {
            InstanceId = $"USB\\VID_0000&PID_000{n}\\{n}",
            DisplayName = name,
            ClassName = cls,
            GameRelevant = cls == "HIDClass",
        },
    };

    private static TopologySnapshot Snapshot(params PortEntry[] ports)
    {
        var hub = new HubGroup { HubKey = "HUB01", DisplayName = "Test hub" };
        hub.Ports.AddRange(ports);
        return new TopologySnapshot { Hubs = { hub } };
    }

    [Fact]
    public void Disabling_the_only_enabled_keyboard_is_flagged_as_last()
    {
        var kb = Port(1, "USB Keyboard", "HIDClass");
        var pad = Port(2, "Xbox Controller", "HIDClass");
        var risks = InputGuard.Assess(Snapshot(kb, pad), new[] { kb.Device!.InstanceId });

        var risk = Assert.Single(risks);
        Assert.Equal("keyboard", risk.Kind);
        Assert.True(risk.IsLastEnabled);
    }

    [Fact]
    public void Disabling_one_of_two_keyboards_is_flagged_but_not_last()
    {
        var kb1 = Port(1, "USB Keyboard", "HIDClass");
        var kb2 = Port(2, "Other Keyboard", "HIDClass");
        var risk = Assert.Single(InputGuard.Assess(Snapshot(kb1, kb2), new[] { kb1.Device!.InstanceId }));
        Assert.False(risk.IsLastEnabled);
    }

    [Fact]
    public void A_keyboard_that_is_already_disabled_does_not_count_as_a_survivor()
    {
        var off = Port(1, "Old Keyboard", "HIDClass", PortState.Disabled);
        var kb = Port(2, "USB Keyboard", "HIDClass");
        var risk = Assert.Single(InputGuard.Assess(Snapshot(off, kb), new[] { kb.Device!.InstanceId }));
        Assert.True(risk.IsLastEnabled);
    }

    [Fact]
    public void Controllers_and_non_input_devices_are_never_flagged()
    {
        var pad = Port(1, "Xbox Controller", "HIDClass");
        var cam = Port(2, "Webcam", "Camera");
        Assert.Empty(InputGuard.Assess(Snapshot(pad, cam), new[] { pad.Device!.InstanceId, cam.Device!.InstanceId }));
    }
}
