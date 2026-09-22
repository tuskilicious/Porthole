using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using USBControl.Core;

namespace USBControl.App.Services;

/// <summary>Bridges a HubGroup snapshot node into the observable UI world.</summary>
public sealed class HubGroupViewModel
{
    public HubGroup Entry { get; }
    public AppController Controller { get; }
    public ObservableCollection<PortViewModel> Ports { get; } = new();

    /// <summary>Zero-based position of this hub in the snapshot (drives the "HUB n" header).</summary>
    public int HubIndex { get; }

    public string DisplayName => Entry.DisplayName;

    /// <summary>Header text: "HUB 3", hair-spaced to fake the letter-spacing WPF text lacks.</summary>
    public string HubHeader => TextStyle.Track($"HUB {HubIndex + 1}");

    /// <summary>The one count under a hub header: "4 of 6 in use".</summary>
    public string UsageText => $"{Ports.Count(p => !p.IsEmpty)} of {Ports.Count} in use";

    public HubGroupViewModel(AppController controller, HubGroup entry, int hubIndex = 0)
    {
        Controller = controller;
        Entry = entry;
        HubIndex = hubIndex;
    }
}

/// <summary>One physical port tile: state, device, photo, editor fields, and actions.</summary>
public sealed class PortViewModel : INotifyPropertyChanged
{
    /// <summary>Raised when a tile asks to be opened in the editor panel (double-click).</summary>
    public static event Action<PortViewModel>? EditorRequested;

    public PortEntry Entry { get; }
    public AppController Controller { get; }

    private double _layoutX;
    private double _layoutY;

    public PortViewModel(AppController controller, PortEntry entry, bool appeared = false)
    {
        Controller = controller;
        Entry = entry;
        Appeared = appeared;
        (_layoutX, _layoutY) = controller.GetPanelPos(entry.PortKey);

        ToggleCommand = new RelayCommand(
            _ => _ = Controller.ToggleAsync(Entry, Entry.State == PortState.Disabled),
            _ => Entry.Device is not null);
        OpenEditorCommand = new RelayCommand(_ => EditorRequested?.Invoke(this));
        CopyDetailsCommand = new RelayCommand(_ => CopyDetails(), _ => Entry.Device is not null);

        // Weak subscription: tiles are rebuilt on every refresh and must not leak.
        PropertyChangedEventManager.AddHandler(Controller, OnControllerChanged,
            nameof(AppController.BusyDeviceIdentity));
        PickPhotoCommand = new RelayCommand(_ => PickPhoto());
        ClearPhotoCommand = new RelayCommand(_ => Controller.ClearDevicePhoto(Entry),
            _ => HasPhoto);
    }

    public ICommand ToggleCommand { get; }
    public ICommand OpenEditorCommand { get; }
    public ICommand PickPhotoCommand { get; }
    public ICommand ClearPhotoCommand { get; }
    public ICommand CopyDetailsCommand { get; }

    /// <summary>Demo mode only: a HardwareId fragment whose device is shown permanently busy.</summary>
    public static string? DemoBusyMatch { get; set; }

    private bool _isSelected;

    /// <summary>True while this tile is the one open in the editor (drives the selection ring).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StateText));
    }

    public UsbDeviceInfo? Device => Entry.Device;

    public string PortKey => Entry.PortKey;

    /// <summary>True when this tile's device was not on screen before the last refresh (drives the fade-in).</summary>
    public bool Appeared { get; }

    public string PortTitle
    {
        get
        {
            var meta = Controller.Store.GetOrCreatePort(Entry.PortKey);
            return string.IsNullOrWhiteSpace(meta.Label) ? $"Port {Entry.PortNumber}" : meta.Label;
        }
    }

    /// <summary>The port label as a small tracked-out caption: "PORT 3" (or the user's label).</summary>
    public string PortCaption => TextStyle.Track(PortTitle.ToUpperInvariant());

    public string HubName
    {
        get
        {
            var hub = Controller.Hubs.FirstOrDefault(h => h.Entry.HubKey == Entry.HubKey);
            return hub?.DisplayName ?? Entry.HubKey;
        }
    }

    /// <summary>
    /// What the tile shows as the device name: the user's name, else the OS product
    /// name, else "Unknown device (VID:PID)" — never a raw USB\VID_... path.
    /// </summary>
    public string DeviceName
    {
        get
        {
            if (Entry.Device is null) return "Empty";
            // "(XINPUT)" is the driver child's suffix, not part of the product name.
            return Regex.Replace(DisplayDeviceName(Entry.Device), @"\s*\(XINPUT\)$", "");
        }
    }

    /// <summary>The untrimmed name, for the tile tooltip.</summary>
    public string DeviceNameFull => Entry.Device is null ? "Empty" : DisplayDeviceName(Entry.Device);

    private static string DisplayDeviceName(UsbDeviceInfo d)
    {
        var name = d.ChildDisplayName ?? d.DisplayName;
        return string.IsNullOrWhiteSpace(name) || LooksLikeUsbPath(name) ? FallbackDeviceName(d) : name;
    }

    /// <summary>True for OS instance paths such as "USB\VID_1A34&amp;PID_F5B2\5&amp;2A1B" that leaked in as a name.</summary>
    private static bool LooksLikeUsbPath(string name) =>
        name.Contains('\\') || (name.Contains("VID_", StringComparison.OrdinalIgnoreCase)
                                && name.Contains("PID_", StringComparison.OrdinalIgnoreCase));

    private static string FallbackDeviceName(UsbDeviceInfo d) =>
        TryVidPid(d, out var vidPid) ? $"Unknown device ({vidPid})" : "Unknown device";

    private static bool TryVidPid(UsbDeviceInfo d, out string vidPid)
    {
        foreach (var source in new[] { d.HardwareId, d.Identity, d.InstanceId })
        {
            var m = Regex.Match(source ?? "", @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");
            if (m.Success)
            {
                vidPid = $"{m.Groups[1].Value}:{m.Groups[2].Value}".ToUpperInvariant();
                return true;
            }
        }
        vidPid = "";
        return false;
    }

    public string StateText => StatusKind switch
    {
        "busy" => "Busy",
        "error" => "Error",
        "disabled" => "Disabled",
        "hub" => "Hub",
        "connected" => "Connected",
        _ => "Empty",
    };

    /// <summary>connected / disabled / error / busy / hub / empty — drives the chip and tile styling.</summary>
    public string StatusKind
    {
        get
        {
            if (Entry.Device is null) return "empty";
            if (IsBusy) return "busy";
            return Entry.State switch
            {
                PortState.Problem => "error",
                PortState.Disabled => "disabled",
                PortState.Hub => "hub",
                _ => IsHub ? "hub" : "connected",
            };
        }
    }

    /// <summary>A power change is in flight for this device (or it is the demo's busy sample).</summary>
    public bool IsBusy =>
        Entry.Device is { } d
        && ((Controller.BusyDeviceIdentity is { } id && id.Equals(d.Identity, StringComparison.OrdinalIgnoreCase))
            || (DemoBusyMatch is { Length: > 0 } m && d.HardwareId.Contains(m, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Why the chip says Error, from the hub's port status (null for every other state, so no tooltip shows).</summary>
    public string? StatusTooltip => StatusKind == "error" ? ExplainError(Entry.Device?.ConnectionStatus ?? 0) : null;

    private static string ExplainError(byte status) => status switch
    {
        2 => "The port could not enumerate this device (failed enumeration). Unplug it and plug it back in, or try another port or cable.",
        3 => "The port reported a general failure with this device. Try another port or cable.",
        4 => "This device drew too much current (over-current) and the port shut it down. Use a powered hub or another port.",
        5 => "There is not enough power for this device on this port. Use a powered hub or another port.",
        6 => "There is not enough USB bandwidth for this device on this controller. Move it to a different port or controller.",
        7 => "This device is behind too many hubs. Plug it into a port closer to the PC.",
        8 => "A high-speed device is plugged into a legacy hub. Use a USB 2.0 or newer hub.",
        _ => "The port reported a problem with this device. Unplug and reconnect it, or try another port; if it keeps failing, check its driver in Device Manager.",
    };

    public bool IsEmpty => Entry.Device is null;
    public bool HasDevice => Entry.Device is not null;
    public bool IsDisabled => Entry.State == PortState.Disabled;
    public bool IsProblem => Entry.State == PortState.Problem;
    public bool IsHub => Entry.Device?.IsHub ?? false;

    /// <summary>Only real, non-hub devices get an on/off switch.</summary>
    public bool HasToggle => Entry.Device is not null && !IsHub;

    /// <summary>Drives the tile's switch: on when a device is present and not disabled.</summary>
    public bool IsOn => Entry.Device is not null && Entry.State != PortState.Disabled;

    public string ToggleTooltip => IsOn
        ? "On — click to disable (Device Manager power state)"
        : "Off — click to enable (Device Manager power state)";

    /// <summary>
    /// Device type for the icon: controller, keyboard, mouse, headset, webcam, hub,
    /// storage, unknown (or empty for a free port). Mouse/keyboard are HID-usage
    /// specific and are checked before the broad game-device net so a "G512 RGB
    /// Mechanical Keyboard" (HIDClass, game-relevant) doesn't get the controller glyph.
    /// </summary>
    public string DeviceKind => Entry.Device is null ? "empty" : DeviceClassifier.Classify(Entry.Device);

    // ---------------- editor: technical details ----------------

    public string VidPid => Entry.Device is { } d && TryVidPid(d, out var v) ? v : "—";

    public string SerialText => string.IsNullOrWhiteSpace(Entry.Device?.Serial) ? "—" : Entry.Device!.Serial;

    public string InstanceId => Entry.Device?.InstanceId is { Length: > 0 } id ? id : "—";

    /// <summary>"USB 2.0 Full-speed" etc., from the negotiated connection speed. "—" when unknown.</summary>
    public string SpeedText => Entry.Device is { } d ? SpeedToText(d.Speed) : "—";

    private static string SpeedToText(byte speed) => speed switch
    {
        0 => "USB 1.0 Low-speed",
        1 => "USB 1.1 Full-speed",
        2 => "USB 2.0 High-speed",
        3 => "USB 3.x SuperSpeed",
        _ => "—",
    };

    /// <summary>"Rear USB 3.2 Gen2 · port 3", with "(front panel)"/"(rear panel)" appended
    /// once the port has been assigned a zone.</summary>
    public string HubPortText
    {
        get
        {
            var text = $"{HubName} · port {Entry.PortNumber}";
            var zone = Controller.Store.GetOrCreatePort(Entry.PortKey).Zone;
            return zone is { Length: > 0 } ? $"{text} ({zone.ToLowerInvariant()} panel)" : text;
        }
    }

    /// <summary>True while this port is assigned to the front-panel grid.</summary>
    public bool IsFrontPanel
    {
        get => Controller.Store.GetOrCreatePort(Entry.PortKey).Zone == "Front";
        set
        {
            Controller.TogglePortZone(Entry);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HubPortText));
        }
    }

    public string DetailsText =>
        $"VID:PID  {VidPid}\nSerial   {SerialText}\nInstance {InstanceId}\nHub/port {HubPortText}";

    private void CopyDetails()
    {
        try
        {
            System.Windows.Clipboard.SetText(DetailsText);
            Controller.StatusText = "Device details copied to the clipboard.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // clipboard briefly locked by another process; nothing to do
        }
    }

    // ---------------- panel layout (free-form view) ----------------

    /// <summary>Canvas X on the panel surface (binds to Canvas.Left).</summary>
    public double LayoutX { get => _layoutX; private set { _layoutX = value; OnPropertyChanged(nameof(LayoutX)); } }

    /// <summary>Canvas Y on the panel surface (binds to Canvas.Top).</summary>
    public double LayoutY { get => _layoutY; private set { _layoutY = value; OnPropertyChanged(nameof(LayoutY)); } }

    /// <summary>Called by the controller after a drop so the tile snaps to the grid.</summary>
    public void SetPosition(double x, double y)
    {
        LayoutX = x;
        LayoutY = y;
    }

    public string? PhotoPath =>
        Entry.Device is null
            ? null
            : Controller.PhotoPathFor(Controller.Store.GetOrCreateDevice(
                Entry.Device.Identity, Entry.Device.InstanceId, Entry.Device.DisplayName).PhotoFile);

    public bool HasPhoto => PhotoPath is not null;

    // ---------------- editor panel fields ----------------

    public string EditPortLabel
    {
        get => PortTitle;
        set
        {
            Controller.RenamePort(Entry, value);
            OnPropertyChanged(nameof(PortTitle));
        }
    }

    public string EditDeviceName
    {
        get => Entry.Device?.DisplayName ?? "";
        set
        {
            Controller.RenameDevice(Entry, value);
            OnPropertyChanged(nameof(DeviceName));
        }
    }

    public string EditNote
    {
        get
        {
            if (Entry.Device is null) return "";
            var meta = Controller.Store.GetOrCreateDevice(Entry.Device.Identity, Entry.Device.InstanceId, Entry.Device.DisplayName);
            return meta.Note;
        }
        set
        {
            if (Entry.Device is null) return;
            var meta = Controller.Store.GetOrCreateDevice(Entry.Device.Identity, Entry.Device.InstanceId, Entry.Device.DisplayName);
            if (meta.Note == value) return;
            meta.Note = value;
            Controller.Store.Save();
        }
    }

    private void PickPhoto()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a picture for this device",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog() != true)
            return;
        Controller.SetDevicePhoto(Entry, dlg.FileName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Loads a photo file into a UI-ready ImageSource, cached per path (decoding is expensive; tiles rebind constantly).</summary>
public sealed class PhotoConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Render the picture desaturated (used for disabled devices).</summary>
    public bool Grayscale { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;
        var gray = Grayscale;
        return Cache.GetOrAdd((gray ? "gray|" : "") + path, _ =>
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // release the file handle immediately
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = 320; // decode at display size, not full resolution
                bmp.EndInit();
                bmp.Freeze(); // thread-safe + no WPF bitmap overhead
                if (!gray)
                    return (ImageSource)bmp;

                var converted = new FormatConvertedBitmap(bmp, PixelFormats.Gray32Float, null, 0);
                converted.Freeze();
                return converted;
            }
            catch
            {
                return null;
            }
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → Visibility, with optional inversion.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var b = value is bool v && v;
        if (Inverse) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Two strings equal (ordinal, case-insensitive) → true; used by the sidebar profile
/// card to compare its own Name against Controller.ActiveProfile ("Live" state).</summary>
public sealed class StringEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        values.Length == 2 && values[0] is string a && values[1] is string b
            && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string/non-null → Visible; used for the "active profile" pill (bound to ActiveProfile).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        (value is string s ? !string.IsNullOrEmpty(s) : value is not null) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Device kind ("controller", "mouse", …) → its line-icon geometry ("Icon" + Kind resource).</summary>
public sealed class KindToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var kind = value as string is { Length: > 0 } k ? k : "unknown";
        var key = "Icon" + char.ToUpperInvariant(kind[0]) + kind[1..];
        return Application.Current.TryFindResource(key) ?? Application.Current.TryFindResource("IconUnknown");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Device kind → its default peripheral icon (the Porthole icon pack), for kinds the pack covers.
/// Several kinds share one drawing: any game-input device (controller/joystick/wheel) reads as
/// "gamepad", and anything recognized-but-not-illustrated (dongle/network) reads as the pack's
/// own generic-device stand-in, "cable". Kinds the pack doesn't cover (hub, unknown, empty) fall
/// through to the vector glyph (TileGlyph/KindToGeometryConverter) instead — Convert returns null,
/// and the target-type check lets one converter instance drive both the Image's Source (null hides
/// it) and the fallback Path's Visibility (Inverse for the "show the vector glyph instead" case).
/// </summary>
public sealed class KindToPeripheralIconConverter : IValueConverter
{
    private static readonly Dictionary<string, string> AssetByKind = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mouse"] = "mouse",
        ["keyboard"] = "keyboard",
        ["headset"] = "headphones",
        ["webcam"] = "webcam",
        ["controller"] = "gamepad",
        ["joystick"] = "gamepad",
        ["wheel"] = "gamepad",
        ["storage"] = "flashdrive",
        ["printer"] = "printer",
        ["microphone"] = "microphone",
        ["phone"] = "phone",
        ["dongle"] = "cable",
        ["network"] = "cable",
    };

    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();

    /// <summary>Inverted for the fallback vector glyph: visible only when there is no peripheral icon.</summary>
    public bool Inverse { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var icon = value is string kind && AssetByKind.TryGetValue(kind, out var asset) ? Load(asset) : null;

        if (targetType == typeof(Visibility))
        {
            var show = Inverse ? icon is null : icon is not null;
            return show ? Visibility.Visible : Visibility.Collapsed;
        }
        return icon;
    }

    private static ImageSource Load(string asset) => Cache.GetOrAdd(asset, static a =>
    {
        var bmp = new BitmapImage(new Uri($"pack://application:,,,/Assets/peripherals/{a}.png"));
        bmp.Freeze();
        return bmp;
    });

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Negates a bool (two-way), so one setting can drive two mutually exclusive radio buttons.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>Small text helpers for the tracked-out caps look.</summary>
public static class TextStyle
{
    /// <summary>
    /// Fakes letter-spacing (~0.08em) by putting a hair space between characters, since WPF
    /// TextBlock has no tracking property.
    /// </summary>
    public static string Track(string text) => string.Join("\u200A", text.ToCharArray());
}
