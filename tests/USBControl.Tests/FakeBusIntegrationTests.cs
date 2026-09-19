using USBControl.Core;
using USBControl.Testing;
using Xunit;

namespace USBControl.Tests;

/// <summary>
/// Integration tests: the FakeUsbBus simulates realistic hardware (Windows-format
/// instance ids, hot-plug, moves, persistent disable) and drives the real
/// AppController pipeline — merge, identity matching, profiles — with no hardware.
/// </summary>
public class FakeBusIntegrationTests : IDisposable
{
    private readonly TestingRig _rig;
    private readonly Dictionary<string, string> _ids;

    public FakeBusIntegrationTests()
    {
        _rig = new TestingRig(FakeRigs.GamingRig(out var ids));
        _ids = ids;
    }

    public void Dispose() => _rig.Dispose();

    private TopologySnapshot Snap() => _rig.Bus.Snapshot();

    // ---------------- enumeration & merge ----------------

    [Fact]
    public void Snapshot_Produces_Realistic_Instance_Ids_And_States()
    {
        var snap = Snap();

        Assert.Equal(2, snap.Hubs.Count);
        Assert.Equal(8, snap.AllPorts.Count());

        var pad = snap.AllPorts.First(p => p.PortKey == "HUB01#1").Device!;
        Assert.Equal("USB\\VID_045E&PID_0B12\\XBOXSERIAL1", pad.InstanceId);
        Assert.Equal("VID_045E&PID_0B12:XBOXSERIAL1", pad.Identity);
        Assert.True(pad.GameRelevant); // XUSB + HID children
        Assert.Equal(PortState.Connected, snap.AllPorts.First(p => p.PortKey == "HUB01#1").State);

        // Serial-less devices get a location tail and a VID/PID-only identity.
        var ds4 = snap.AllPorts.First(p => p.PortKey == "HUB01#2").Device!;
        Assert.Matches(@"^USB\\VID_054C&PID_05C4\\6&[0-9a-f]+&0&2$", ds4.InstanceId);
        Assert.Equal("VID_054C&PID_05C4", ds4.Identity);

        // Empty ports stay empty.
        Assert.Null(snap.AllPorts.First(p => p.PortKey == "HUB01#3").Device);
    }

    [Fact]
    public async Task Rename_Reattaches_After_Replug_On_Another_Port()
    {
        await _rig.SettleAsync();
        var port = _rig.Controller.Hubs.SelectMany(h => h.Ports).First(p => p.PortKey == "HUB01#1");
        _rig.Controller.RenameDevice(port.Entry, "My main pad");
        await _rig.SettleAsync();

        // Physically move the pad to a different port (fresh location, serial identity kept).
        _rig.Bus.Move("HUB01#1", "HUB02#3");
        await _rig.SettleAsync();

        var moved = _rig.Controller.Hubs.SelectMany(h => h.Ports)
            .First(p => p.PortKey == "HUB02#3");
        Assert.Equal("My main pad", moved.Device!.DisplayName);
        Assert.Equal("My main pad", _rig.Store.Data.Devices["VID_045E&PID_0B12:XBOXSERIAL1"].FriendlyName);
    }

    [Fact]
    public async Task Serialless_Device_Keeps_Identity_Across_Moves()
    {
        await _rig.SettleAsync();
        _rig.Controller.RenameDevice(
            _rig.Controller.Hubs.SelectMany(h => h.Ports).First(p => p.PortKey == "HUB01#2").Entry,
            "Couch DS4");
        await _rig.SettleAsync();

        _rig.Bus.Move("HUB01#2", "HUB02#4"); // serial-less → new instance id
        await _rig.SettleAsync();

        var moved = _rig.Controller.Hubs.SelectMany(h => h.Ports).First(p => p.PortKey == "HUB02#4");
        Assert.Equal("Couch DS4", moved.Device!.DisplayName);
    }

    // ---------------- disable semantics ----------------

    [Fact]
    public async Task Disable_Persists_Across_Unplug_Replug_And_Reboot()
    {
        await _rig.SettleAsync();
        var stickId = _ids["stick"];

        // Disable via the app (the real service path).
        await _rig.Controller.ToggleAsync(
            _rig.Controller.Hubs.SelectMany(h => h.Ports).First(p => p.Device is { } d && d.InstanceId == stickId).Entry,
            enable: false);
        await _rig.SettleAsync();
        Assert.Equal(PortState.Disabled,
            Snap().AllPorts.First(p => p.Device is { } d && d.InstanceId == stickId).State);

        // Unplug and replug → still disabled (CM_DISABLE_PERSISTENT semantics).
        _rig.Bus.Unplug("HUB02#1");
        await _rig.SettleAsync();
        _rig.Bus.Plug(FakeRigs.FlightStick("HOTAS001"), "HUB02#1");
        await _rig.SettleAsync();
        Assert.Equal(PortState.Disabled, Snap().AllPorts.First(p => p.PortKey == "HUB02#1").State);

        // Reboot → still disabled; nothing else changed state.
        _rig.Bus.Reboot();
        await _rig.SettleAsync();
        Assert.Equal(PortState.Disabled, Snap().AllPorts.First(p => p.PortKey == "HUB02#1").State);
    }

    // ---------------- profiles ----------------

    [Fact]
    public async Task Profile_Apply_Enables_In_Order_And_Disables_Rest()
    {
        await _rig.SettleAsync();

        // Capture "Flight sim": current state = everything enabled.
        var profile = _rig.Controller.CaptureProfile("Flight sim");
        await _rig.SettleAsync();

        // User disables pad + DS4 via the app.
        foreach (var key in new[] { "HUB01#1", "HUB01#2" })
            await _rig.Controller.ToggleAsync(
                _rig.Controller.Hubs.SelectMany(h => h.Ports).First(p => p.PortKey == key).Entry, enable: false);
        await _rig.SettleAsync();

        // Reorder: profile should enable DS4 before pad (couch co-op ordering matters).
        var ds4Entry = profile.Devices.First(d => d.Identity == "VID_054C&PID_05C4");
        var padEntry = profile.Devices.First(d => d.Identity == "VID_045E&PID_0B12:XBOXSERIAL1");
        ds4Entry.Order = 0;
        padEntry.Order = 1;

        _rig.Power.Calls.Clear();
        await _rig.Controller.ApplyProfileAsync(profile);
        await _rig.SettleAsync();

        var enables = _rig.Power.Calls.Where(c => c.Enable).Select(c => c.InstanceId).ToList();
        Assert.Equal(2, enables.Count);
        Assert.Equal(_ids["ds4"], enables[0]);      // profile order respected
        Assert.Equal(_ids["pad1"], enables[1]);

        // Everything still enabled stayed enabled (no redundant disable of stick/leverless).
        Assert.DoesNotContain(_rig.Power.Calls, c => !c.Enable && c.InstanceId == _ids["stick"]);

        // Final bus state: all four connected again.
        var states = Snap().AllPorts.Where(p => p.Device is { IsHub: false })
            .ToDictionary(p => p.Device!.InstanceId, p => p.State);
        Assert.All(states.Values, s => Assert.Equal(PortState.Connected, s));
    }

    [Fact]
    public async Task Profile_Capture_After_Disables_Records_Enabled_False()
    {
        await _rig.SettleAsync();
        await _rig.Controller.ToggleAsync(
            _rig.Controller.Hubs.SelectMany(h => h.Ports).First(p => p.PortKey == "HUB02#2").Entry,
            enable: false); // leverless off
        await _rig.SettleAsync();

        var profile = _rig.Controller.CaptureProfile("No fightstick");
        var leverless = Assert.Single(profile.Devices, d => d.Identity == "VID_0F0D&PID_0092");
        Assert.False(leverless.Enabled);
    }

    // ---------------- failure paths ----------------

    [Fact]
    public async Task Toggle_Failure_Reports_Error_And_State_Unchanged()
    {
        await _rig.SettleAsync();
        _rig.Power.FailOn(_ids["ds4"]);

        await _rig.Controller.ToggleAsync(
            _rig.Controller.Hubs.SelectMany(h => h.Ports).First(p => p.PortKey == "HUB01#2").Entry,
            enable: false);
        await _rig.SettleAsync();

        Assert.Contains("simulated failure", _rig.Controller.StatusText);
        Assert.Equal(PortState.Connected, Snap().AllPorts.First(p => p.PortKey == "HUB01#2").State);
    }

    [Fact]
    public async Task Profile_Apply_With_Failing_Device_Reports_Errors_But_Continues()
    {
        await _rig.SettleAsync();
        var profile = _rig.Controller.CaptureProfile("All off");
        foreach (var d in profile.Devices)
            d.Enabled = false; // everything should be disabled
        await _rig.SettleAsync();

        _rig.Power.FailOn(_ids["leverless"]);
        await _rig.Controller.ApplyProfileAsync(profile);
        await _rig.SettleAsync();

        Assert.Contains("applied with errors", _rig.Controller.StatusText);
        // The other two disables still went through.
        Assert.Equal(PortState.Disabled, Snap().AllPorts.First(p => p.PortKey == "HUB02#1").State);
        Assert.Equal(PortState.Connected, Snap().AllPorts.First(p => p.PortKey == "HUB02#2").State);
    }

    // ---------------- hot-plug watcher ----------------

    [Fact]
    public async Task Bus_Changes_Trigger_Controller_Refresh()
    {
        await _rig.SettleAsync();
        Assert.Equal(2, _rig.Controller.Hubs.Count);

        _rig.Bus.AddHub("Front add-in card", 2);
        _rig.Bus.Plug(FakeDeviceSpec.Hid("1235", "8242", "New Gadget"), "HUB03#1");
        await _rig.SettleAsync();

        var newPort = _rig.Controller.Hubs.SelectMany(h => h.Ports).FirstOrDefault(p => p.PortKey == "HUB03#1");
        Assert.NotNull(newPort);
        Assert.Equal("New Gadget", newPort!.Device!.DisplayName);

        _rig.Bus.Unplug("HUB03#1");
        await _rig.SettleAsync();
        // Empty ports are hidden by default, so the tile disappears entirely.
        Assert.DoesNotContain(_rig.Controller.Hubs.SelectMany(h => h.Ports), p => p.PortKey == "HUB03#1");
    }

    // ---------------- panel layout (via controller, no UI) ----------------

    [Fact]
    public async Task Panel_Positions_Persist_Across_Controller_Rebuilds()
    {
        await _rig.SettleAsync();
        _rig.Controller.MovePortTo(
            _rig.Controller.Hubs.SelectMany(h => h.Ports).First(p => p.PortKey == "HUB01#1"),
            500, 300);
        await _rig.SettleAsync();

        // Simulate a restart: fresh store + controller over the same bus AND store dir.
        using var rig2 = new TestingRig(_rig.Bus, rootDir: _rig.RootDir);
        await rig2.SettleAsync();

        var pos = rig2.Controller.GetPanelPos("HUB01#1");
        Assert.Equal(PanelLayoutMath.SnapX(500), pos.X);
        Assert.Equal(PanelLayoutMath.SnapY(300), pos.Y);
    }
}
