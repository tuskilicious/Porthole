using USBControl.Core;
using Xunit;

namespace USBControl.Tests;

public class DeviceIdentityTests
{
    [Theory]
    [InlineData("USB\\VID_045E&PID_0B12\\6&2f3827&0&2", "VID_045E&PID_0B12", null)]
    [InlineData("USB\\VID_045E&PID_02FF\\5&2e3f&0&1", "VID_045E&PID_02FF", null)]
    [InlineData("USB\\VID_045E&PID_0B12\\ABC123", "VID_045E&PID_0B12", "ABC123")]
    [InlineData("USB\\VID_0F0D&PID_00C1\\00A0B0C0D0", "VID_0F0D&PID_00C1", "00A0B0C0D0")]
    public void Parse_Extracts_VidPid_And_Serial(string instanceId, string vidPid, string? serial)
    {
        var (v, s) = DeviceIdentity.Parse(instanceId);
        Assert.Equal(vidPid, v);
        Assert.Equal(serial, s);
    }

    [Fact]
    public void Build_Uses_Serial_When_Present()
    {
        Assert.Equal("VID_045E&PID_0B12:ABC123", DeviceIdentity.Build("USB\\VID_045E&PID_0B12\\ABC123"));
    }

    [Fact]
    public void Build_Falls_Back_To_VidPid_Only()
    {
        Assert.Equal("VID_045E&PID_0B12", DeviceIdentity.Build("USB\\VID_045E&PID_0B12\\6&2f3827&0&2"));
    }

    [Fact]
    public void Build_Falls_Back_To_InstanceId_For_NonUsb()
    {
        Assert.Equal("ACPI\\PNP0501\\1", DeviceIdentity.Build("ACPI\\PNP0501\\1"));
    }

    [Fact]
    public void MatchScore_InstanceMatch_Beats_IdentityMatch()
    {
        var stored = "VID_045E&PID_0B12:SER1";
        Assert.Equal(3, DeviceIdentity.MatchScore("USB\\VID_045E&PID_0B12\\SER1", stored,
            "USB\\VID_045E&PID_0B12\\SER1"));
        Assert.Equal(2, DeviceIdentity.MatchScore("USB\\VID_045E&PID_0B12\\SER1", stored));
        // Same VID/PID but different serial → a different physical device, no match.
        Assert.Equal(0, DeviceIdentity.MatchScore("USB\\VID_045E&PID_0B12\\SER2", stored));
        Assert.Equal(0, DeviceIdentity.MatchScore("USB\\VID_045E&PID_0B13\\SER1", stored));
    }

    [Fact]
    public void MatchScore_Moving_Ports_Keeps_Identity()
    {
        // Same pad on a different hub/port (different location tail) still scores 2.
        Assert.Equal(2, DeviceIdentity.MatchScore("USB\\VID_045E&PID_0B12\\SER1", "VID_045E&PID_0B12:SER1"));
    }
}
