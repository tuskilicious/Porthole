using USBControl.Core;

namespace USBControl.App.Services;

/// <summary>Maps a device to a type ("controller", "keyboard", "mouse", "headset", …) from its class, name and HID usage.</summary>
public static class DeviceClassifier
{
    public static string Classify(UsbDeviceInfo d)
    {
        if (d.IsHub)
            return "hub";

        var hw = d.HardwareId ?? "";
        var cls = d.ClassName ?? "";
        var name = d.ChildDisplayName ?? d.DisplayName ?? "";
        bool HwHas(string s) => hw.Contains(s, StringComparison.OrdinalIgnoreCase);
        bool NameHas(string s) => name.Contains(s, StringComparison.OrdinalIgnoreCase);

        // HID usage: pointing and keyboard devices (checked before controllers, so a
        // game-relevant "G512 RGB Mechanical Keyboard" doesn't get the controller glyph).
        if (cls is "HIDClass" or "HID" || NameHas("Mouse") || NameHas("Keyboard"))
        {
            if (HwHas("MusHID") || HwHas("Mouse") || HwHas("VID_046D&PID_C08") ||
                NameHas("Mouse") || NameHas("Trackball"))
                return "mouse";
            if (HwHas("Keyboard") || NameHas("Keyboard"))
                return "keyboard";
        }

        if (cls is "AudioEndpoint" or "MEDIA" or "AudioProcessingObject" ||
            NameHas("Headset") || NameHas("Headphone") || NameHas("Speaker") ||
            NameHas("Microphone") || NameHas("Audio") || NameHas("Sound"))
            return "headset";

        if (cls is "Image" or "Camera" || HwHas("Camera") ||
            NameHas("Webcam") || NameHas("Camera"))
            return "webcam";

        if (cls is "WPD" or "DiskDrive" or "USBStorage" or "SCSIAdapter" ||
            (HwHas("SCSI") && HwHas("Disk")) ||
            NameHas("Storage") || NameHas("SSD") || NameHas("Drive"))
            return "storage";

        // Controllers: gamepads, fightsticks, HOTAS, joysticks — HID game usage,
        // XINPUT children, or an explicit controller flag from the hardware layer.
        if (d.IsController || d.GameRelevant || cls is "XInput" or "GameControl" ||
            NameHas("Controller") || NameHas("Fightstick") || NameHas("Gamepad") ||
            NameHas("Joystick") || NameHas("HOTAS") || HwHas("IG_"))
            return "controller";

        return "unknown";
    }
}

/// <summary>
/// Lockout protection: a USB keyboard or mouse that is about to be disabled needs an explicit
/// "yes", and a much louder warning when it is the last enabled one of its kind. Persistent
/// disable survives a reboot, so losing your only input device can mean safe mode.
/// </summary>
public static class InputGuard
{
    public sealed record Risk(string Kind, string Name, bool IsLastEnabled);

    /// <summary>Keyboards/mice among the devices that would end up disabled.</summary>
    public static IReadOnlyList<Risk> Assess(TopologySnapshot snapshot, IEnumerable<string> instanceIdsToDisable)
    {
        var doomed = new HashSet<string>(instanceIdsToDisable, StringComparer.OrdinalIgnoreCase);
        var enabled = snapshot.AllPorts
            .Where(p => p.Device is { IsHub: false } && p.State != PortState.Disabled)
            .Select(p => (Device: p.Device!, Kind: DeviceClassifier.Classify(p.Device!)))
            .Where(x => x.Kind is "keyboard" or "mouse")
            .ToList();

        var risks = new List<Risk>();
        foreach (var kind in new[] { "keyboard", "mouse" })
        {
            var ofKind = enabled.Where(x => x.Kind == kind).ToList();
            var survivors = ofKind.Count(x => !doomed.Contains(x.Device.InstanceId));
            foreach (var x in ofKind.Where(x => doomed.Contains(x.Device.InstanceId)))
                risks.Add(new Risk(kind, string.IsNullOrWhiteSpace(x.Device.DisplayName) ? x.Device.InstanceId : x.Device.DisplayName,
                    IsLastEnabled: survivors == 0));
        }
        return risks;
    }

    public static string Describe(IReadOnlyList<Risk> risks)
    {
        var last = risks.Where(r => r.IsLastEnabled).ToList();
        var names = string.Join(", ", risks.Select(r => $"{r.Name} ({r.Kind})"));
        if (last.Count == 0)
            return $"This will disable: {names}.\n\nContinue?";

        var kinds = string.Join(" and ", last.Select(r => r.Kind).Distinct());
        return $"This would disable your last enabled USB {kinds}: {names}.\n\n" +
               "If this PC has no other keyboard or mouse (built-in, Bluetooth or PS/2), you could lose control of it, " +
               "and the disable survives a reboot. Undo it with \"Enable all disabled\" or in Device Manager.\n\nDisable anyway?";
    }
}
