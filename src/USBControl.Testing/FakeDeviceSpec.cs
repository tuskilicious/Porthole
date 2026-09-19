namespace USBControl.Testing;

/// <summary>Description of a simulated device that can be plugged into a fake port.</summary>
public sealed record FakeDeviceSpec
{
    public required string Vid { get; init; }          // "045E"
    public required string Pid { get; init; }          // "0B12"
    public string? Serial { get; init; }
    public required string Product { get; init; }
    public string Manufacturer { get; init; } = "";
    public string ClassName { get; init; } = "HIDClass";

    /// <summary>Functional child devnodes, e.g. ["HID"] or ["XUSB", "HID"].</summary>
    public IReadOnlyList<string> Children { get; init; } = Array.Empty<string>();

    public bool IsHub { get; init; }
    public byte Speed { get; init; } = 2;

    public static FakeDeviceSpec Hid(string vid, string pid, string product, string? serial = null) =>
        new() { Vid = vid, Pid = pid, Product = product, Serial = serial, Children = new[] { "HID" } };
}
