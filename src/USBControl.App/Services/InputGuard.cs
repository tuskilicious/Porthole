using System.Text.RegularExpressions;
using USBControl.Core;

namespace USBControl.App.Services;

/// <summary>
/// Maps a device to a type (controller, keyboard, mouse, headset, microphone, webcam, dongle, joystick,
/// wheel, network, phone, printer, storage, hub, unknown) from its class, name and HID usage.
/// </summary>
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
        bool NameMatches(string pattern) => Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase);

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

        // Wireless receivers and Bluetooth radios (Unifying, Lightspeed, Xbox Wireless Adapter, ...). Checked before network
        // cards because some of these enumerate under the "Net" class.
        if (cls is "Bluetooth" || NameMatches(@"receiver|dongle|unifying|lightspeed|bluetooth|xbox wireless adapter|\bnano\b"))
            return "dongle";

        // Network adapters (Wi-Fi / Ethernet). Anything that says Wi-Fi, WLAN, 802.11 or Ethernet
        // (or the "Net" class) is a network card.
        if (cls is "Net" || NameMatches(@"wi-?fi|wlan|802\.11|ethernet|\bnetwork\b|\blan\b"))
            return "network";

        // Microphones, before the general audio bucket ("Microphone" contains "phone").
        if (NameHas("Microphone") || NameMatches(@"\bmic\b"))
            return "microphone";

        if (cls is "AudioEndpoint" or "MEDIA" or "AudioProcessingObject" ||
            NameHas("Headset") || NameHas("Headphone") || NameHas("Speaker") ||
            NameHas("Audio") || NameHas("Sound"))
            return "headset";

        if (cls is "Image" or "Camera" || HwHas("Camera") ||
            NameHas("Webcam") || NameHas("Camera"))
            return "webcam";

        if (cls is "Printer" or "PrintQueue" || NameHas("Printer"))
            return "printer";

        // Phones show up as portable devices (WPD), so check them before storage.
        if (NameMatches(@"\b(iphone|ipad|android|pixel|galaxy|phone)\b"))
            return "phone";

        // Racing wheels and pedals, flight sticks / HOTAS, before the general controller bucket.
        if (NameMatches(@"\bwheel\b|racing|pedals?\b"))
            return "wheel";
        if (NameMatches(@"hotas|joystick|flight|throttle|yoke|rudder"))
            return "joystick";

        if (cls is "WPD" or "DiskDrive" or "USBStorage" or "SCSIAdapter" ||
            (HwHas("SCSI") && HwHas("Disk")) ||
            NameHas("Storage") || NameHas("SSD") || NameHas("Drive"))
            return "storage";

        // Controllers: gamepads, fightsticks, XINPUT children, or an explicit controller flag
        // from the hardware layer.
        if (d.IsController || d.GameRelevant || cls is "XInput" or "GameControl" ||
            NameHas("Controller") || NameHas("Fightstick") || NameHas("Gamepad") ||
            HwHas("IG_"))
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
