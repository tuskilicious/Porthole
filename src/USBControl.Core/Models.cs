namespace USBControl.Core;

public enum PortState
{
    Empty,
    Connected,
    Disabled,
    Problem,
    Hub,
}

public sealed class UsbDeviceInfo
{
    public string InstanceId { get; set; } = "";
    public string Identity { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string HardwareId { get; set; } = "";
    public string Serial { get; set; } = "";
    public string ClassName { get; set; } = "";
    public bool IsHub { get; set; }
    public bool IsController { get; set; }
    public byte Speed { get; set; }

    /// <summary>True when the device (or one of its child devnodes) is a HID / game controller class device.</summary>
    public bool GameRelevant { get; set; }

    /// <summary>Set by the hardware layer when the USB devnode is presently disabled.</summary>
    public bool HasDisabledDevnode { get; set; }

    /// <summary>Friendly display name found on a functional child devnode (e.g. the XINPUT child).</summary>
    public string? ChildDisplayName { get; set; }
}

public sealed class PortEntry
{
    public string HubKey { get; set; } = "";
    public int PortNumber { get; set; }
    public string PortKey => $"{HubKey}#{PortNumber}";
    public PortState State { get; set; }
    public UsbDeviceInfo? Device { get; set; }
}

public sealed class HubGroup
{
    public string HubKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<PortEntry> Ports { get; set; } = new();
}

public sealed class TopologySnapshot
{
    public List<HubGroup> Hubs { get; set; } = new();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public IEnumerable<PortEntry> AllPorts => Hubs.SelectMany(h => h.Ports);
}

public sealed class DeviceMeta
{
    public string Identity { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string Note { get; set; } = "";
    public string? PhotoFile { get; set; }
    public bool LastKnownEnabled { get; set; } = true;
    public DateTime LastSeenUtc { get; set; }
}

public sealed class PortMeta
{
    public string PortKey { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Hidden { get; set; }
}

/// <summary>Position of a port tile on the free-form panel (mirrors the case's rear layout).</summary>
public sealed class PortLayout
{
    public string PortKey { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class ProfileEntry
{
    public string Identity { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public int Order { get; set; }
}

public sealed class Profile
{
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public List<ProfileEntry> Devices { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AppSettings
{
    public bool ShowAllDevices { get; set; }
    public bool ShowHiddenPorts { get; set; }
    public bool ShowEmptyPorts { get; set; }
    public bool UsePanelLayout { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public string Theme { get; set; } = "Dark";

    /// <summary>Neon accent palette name (see App: Accents catalog). One of Cyan/Violet/Toxic/Blood/Amber.</summary>
    public string Accent { get; set; } = "Cyan";

    public string? LastProfile { get; set; }
}

public sealed class AppData
{
    public Dictionary<string, DeviceMeta> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PortMeta> Ports { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PortLayout> PanelLayout { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

public sealed record ProfileOp(string InstanceId, bool Enable, string DisplayName);

public sealed record ProfileApplyReport(int Enabled, int Disabled, List<string> Errors)
{
    public bool Success => Errors.Count == 0;
}
