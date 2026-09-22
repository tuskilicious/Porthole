using System.Runtime.InteropServices;
using System.Text;
using USBControl.Core;

namespace USBControl.Hardware;

/// <summary>
/// Enumerates the physical USB tree (controllers → hubs → ports → devices) using the
/// same user-mode hub IOCTLs as Microsoft's USBView sample, and correlates each port's
/// device to its PnP devnode via the connection driver-key name (which is the device
/// instance id). No kernel driver or WDK dependency needed.
/// </summary>
public sealed class UsbTopologyService : ITopologyService, IDisposable
{
    private const int USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = 260;
    private static readonly uint IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION =
        (0x22u << 16) | (0u << 14) | (USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION << 2) | 0u;

    // Raises Changed on device arrival/removal (WM_DEVICECHANGE), so the UI stays live without
    // polling. Changed fires from the watcher's own debounce task, not the UI thread — callers
    // must marshal back before touching anything UI-bound (see AppController.OnTopologyChanged).
    private readonly DeviceChangeWatcher _watcher = new();

    public UsbTopologyService()
    {
        _watcher.Changed += () => Changed?.Invoke();
        _watcher.Start();
    }

    public void Dispose() => _watcher.Dispose();

    public event Action? Changed;

    public TopologySnapshot Snapshot()
    {
        var snapshot = new TopologySnapshot();
        var hubIndex = 0;

        Diag.Log("snapshot: begin");
        foreach (var hubPath in EnumerateHubPaths())
        {
            hubIndex++;
            Diag.Log($"snapshot: hub[{hubIndex}] {hubPath}");
            try
            {
                AddHub(snapshot, hubPath, hubIndex);
            }
            catch (Exception ex)
            {
                Diag.Log($"snapshot: hub {hubPath} failed: {ex.Message}");
            }
        }

        Diag.Log($"snapshot: done, hubs={snapshot.Hubs.Count}, ports={snapshot.AllPorts.Count()}");
        return snapshot;
    }

    // ---------------- controllers → root hubs → downstream hubs ----------------

    private static IEnumerable<string> EnumerateHubPaths()
    {
        var paths = new List<string>();

        foreach (var path in EnumerateInterfacePaths(Native.GuidDevInterfaceUsbHostController))
        {
            try
            {
                var rootHub = GetRootHubName(path);
                if (!string.IsNullOrEmpty(rootHub))
                {
                    // The driver returns a bare symbolic-link name (USB#ROOT_HUB30#…#{guid});
                    // CreateFileW only opens it with the \\?\ device-path prefix.
                    paths.Add(rootHub.StartsWith("\\\\?\\", StringComparison.Ordinal)
                        ? rootHub
                        : "\\\\?\\" + rootHub);
                }
            }
            catch
            {
                // skip controller
            }
        }

        Diag.Log($"controllers: {paths.Count} root hub(s) found");

        // BFS: open each hub, ask every port for a downstream hub name.
        var queue = new Queue<string>(paths);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0)
        {
            var hubPath = queue.Dequeue();
            if (!seen.Add(hubPath))
                continue;
            result.Add(hubPath);

            try
            {
                var hub = OpenHub(hubPath);
                if (hub == IntPtr.Zero)
                    continue;

                try
                {
                    if (GetNodeInformation(hub, out var nodeInfo))
                    {
                        var portCount = nodeInfo.HubDescriptor_bNumberOfPorts;
                        for (uint port = 1; port <= portCount; port++)
                        {
                            try
                            {
                                var childHub = GetChildHubName(hub, port);
                                if (!string.IsNullOrEmpty(childHub))
                                {
                                    // Child names are bare symbolic links too — CreateFileW
                                    // needs the \\?\ device-path prefix, same as root hubs.
                                    queue.Enqueue(childHub.StartsWith("\\\\?\\", StringComparison.Ordinal)
                                        ? childHub
                                        : "\\\\?\\" + childHub);
                                }
                            }
                            catch
                            {
                                // skip port
                            }
                        }
                    }
                }
                finally
                {
                    Native.CloseHandle(hub);
                }
            }
            catch
            {
                // skip hub
            }
        }

        Diag.Log($"hubs after BFS: {result.Count}");
        return result;
    }

    private static string GetRootHubName(string controllerPath)
    {
        var handle = Native.OpenDevice(controllerPath);
        if (handle == new IntPtr(-1) || handle == IntPtr.Zero)
            return "";

        try
        {
            // USB_ROOT_HUB_NAME: ULONG ActualLength(4); WCHAR RootHubName[] → string at offset 4.
            // Pass a real buffer like USBView does: a zero-size sizing call returns FALSE
            // without setting bytesReturned on some USB stacks, which used to make every
            // controller (and therefore the whole tree) vanish.
            var size = 512;
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                if (!Native.DeviceIoControl(handle, Native.IOCTL_USB_GET_ROOT_HUB_NAME,
                        IntPtr.Zero, 0, ptr, size, out int needed, IntPtr.Zero))
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err != 122 /* ERROR_INSUFFICIENT_BUFFER */ || needed is <= 0 or > 65536)
                    {
                        Diag.Log($"root hub name: ioctl failed, err={err}");
                        return "";
                    }

                    // Buffer was too small — retry with the size the driver asked for.
                    Marshal.FreeHGlobal(ptr);
                    ptr = Marshal.AllocHGlobal(needed);
                    if (!Native.DeviceIoControl(handle, Native.IOCTL_USB_GET_ROOT_HUB_NAME,
                            IntPtr.Zero, 0, ptr, needed, out _, IntPtr.Zero))
                    {
                        Diag.Log($"root hub name: retry failed, err={Marshal.GetLastWin32Error()}");
                        return "";
                    }
                }

                var name = ReadUnicodeAfterHeader(ptr, 4);
                Diag.Log($"root hub name: '{name}'");
                return name;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static string GetChildHubName(IntPtr hubHandle, uint portIndex)
    {
        // USB_NODE_CONNECTION_NAME header: ConnectionIndex(4) + ActualLength(4) = 8, then WCHAR[]
        const int headerSize = 8;
        var size = headerSize + 512;
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(ptr, 0, unchecked((int)portIndex)); // ConnectionIndex

            if (!Native.DeviceIoControl(hubHandle, Native.IOCTL_USB_GET_NODE_CONNECTION_NAME,
                    ptr, size, ptr, size, out _, IntPtr.Zero))
                return "";

            // IOCTL_USB_GET_NODE_CONNECTION_NAME returns USB_NODE_CONNECTION_NAME:
            // ULONG ConnectionIndex; ULONG ActualLength; WCHAR NodeName[]
            // wchar offset = 4 + 4 = 8
            return ReadWideStringAt(ptr, 8);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static bool GetNodeInformation(IntPtr hubHandle, out Native.USB_NODE_INFORMATION nodeInfo)
    {
        nodeInfo = default;
        var size = Marshal.SizeOf<Native.USB_NODE_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (!Native.DeviceIoControl(hubHandle, Native.IOCTL_USB_GET_NODE_INFORMATION,
                    IntPtr.Zero, 0, ptr, size, out _, IntPtr.Zero))
                return false;

            nodeInfo = Marshal.PtrToStructure<Native.USB_NODE_INFORMATION>(ptr);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static IntPtr OpenHub(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return IntPtr.Zero;
        var handle = Native.OpenDevice(devicePath);
        if (handle == new IntPtr(-1))
            return IntPtr.Zero;
        return handle;
    }

    // ---------------- per-hub port walk ----------------

    private void AddHub(TopologySnapshot snapshot, string hubPath, int hubIndex)
    {
        var handle = OpenHub(hubPath);
        if (handle == IntPtr.Zero)
        {
            Diag.Log($"hub {hubPath}: open failed, err={Marshal.GetLastWin32Error()}");
            return;
        }

        try
        {
            if (!GetNodeInformation(handle, out var nodeInfo))
            {
                Diag.Log($"hub {hubPath}: node information failed, err={Marshal.GetLastWin32Error()}");
                return;
            }
            var portCount = nodeInfo.HubDescriptor_bNumberOfPorts;
            if (portCount is 0 or > 32)
            {
                Diag.Log($"hub {hubPath}: implausible port count {portCount}");
                return;
            }

            var hubInstanceId = DevicePathToInstanceId(hubPath);
            var hubGroup = new HubGroup
            {
                HubKey = hubInstanceId is { Length: > 0 } ? hubInstanceId : $"HUB{hubIndex}",
                DisplayName = DescribeHub(handle, nodeInfo, hubInstanceId, hubIndex),
            };

            for (uint port = 1; port <= portCount; port++)
            {
                var entry = DescribePort(handle, port, hubGroup.HubKey);
                hubGroup.Ports.Add(entry);
            }

            lock (snapshot.Hubs)
            {
                snapshot.Hubs.Add(hubGroup);
            }
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static string DescribeHub(IntPtr handle, Native.USB_NODE_INFORMATION nodeInfo,
        string? hubInstanceId, int hubIndex)
    {            var portCount = nodeInfo.HubDescriptor_bNumberOfPorts;
            if (!string.IsNullOrEmpty(hubInstanceId) &&
                hubInstanceId.StartsWith("USB\\ROOT_HUB", StringComparison.OrdinalIgnoreCase))
            {
                // USB\ROOT_HUB30\4&... → "USB 3.x root hub"; the controller name is the group header.
                var speed = hubInstanceId.Contains("ROOT_HUB31") ? "3.2 (Gen2)"
                          : hubInstanceId.Contains("ROOT_HUB30") ? "3.x"
                          : hubInstanceId.Contains("ROOT_HUB20") ? "2.0" : "";
                return string.IsNullOrEmpty(speed)
                    ? $"Root hub · {portCount} ports"
                    : $"USB {speed} root hub · {portCount} ports";
            }

            return $"Hub {hubIndex} · {portCount} ports";
    }

    private PortEntry DescribePort(IntPtr hubHandle, uint portIndex, string hubKey)
    {
        var entry = new PortEntry { HubKey = hubKey, PortNumber = unchecked((int)portIndex), State = PortState.Empty };

        var size = Marshal.SizeOf<Native.USB_NODE_CONNECTION_INFORMATION_EX>() + 2048;
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(ptr, 0, unchecked((int)portIndex));

            // Port-index IOCTLs take the same buffer as input AND output; the input must
            // carry ConnectionIndex (a NULL input buffer fails with ERROR_INVALID_PARAMETER).
            if (!Native.DeviceIoControl(hubHandle, Native.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX,
                    ptr, size, ptr, size, out _, IntPtr.Zero))
            {
                Diag.Log($"port {hubKey}#{portIndex}: connection info failed, err={Marshal.GetLastWin32Error()}");
                return entry;
            }

            var info = Marshal.PtrToStructure<Native.USB_NODE_CONNECTION_INFORMATION_EX>(ptr);
            if (info.ConnectionStatus == 0 /* NoDeviceConnected */ && info.DeviceDescriptor.idVendor == 0)
            {
                Diag.Log($"port {hubKey}#{portIndex}: empty (status={info.ConnectionStatus}, vid=0x{info.DeviceDescriptor.idVendor:X4})");
                return entry; // empty port
            }

            var descriptor = info.DeviceDescriptor;
            var instanceId = GetConnectionInstanceId(hubHandle, portIndex);

            var device = new UsbDeviceInfo
            {
                InstanceId = instanceId ?? "",
                Identity = instanceId is { Length: > 0 } ? DeviceIdentity.Build(instanceId) : $"VID_{descriptor.idVendor:X4}&PID_{descriptor.idProduct:X4}",
                IsHub = info.DeviceIsHub != 0,
                Speed = info.Speed,
                ConnectionStatus = (byte)info.ConnectionStatus,
            };

            device.HardwareId = $"USB\\VID_{descriptor.idVendor:X4}&PID_{descriptor.idProduct:X4}";

            // Product string straight from the descriptor chain (works even when the
            // devnode lookup fails).
            var product = GetProductString(hubHandle, portIndex, descriptor.iProduct);
            if (instanceId is { Length: > 0 })
                EnrichFromDevnode(device, instanceId, product);
            else
                device.DisplayName = product ?? device.HardwareId;

            if (info.ConnectionStatus != 1 /* DeviceConnected */)
            {
                entry.State = PortState.Problem;
            }
            else if (device.IsHub)
            {
                entry.State = PortState.Hub;
            }
            else if (device.HasDisabledDevnode)
            {
                entry.State = PortState.Disabled;
            }
            else
            {
                entry.State = PortState.Connected;
            }

            entry.Device = device;
            Diag.Log($"port {hubKey}#{portIndex}: '{device.DisplayName}' state={entry.State} inst='{device.InstanceId}'");
            return entry;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static string? GetConnectionInstanceId(IntPtr hubHandle, uint portIndex)
    {
        // USB_NODE_CONNECTION_DRIVERKEY_NAME header: ConnectionIndex(4) + ActualLength(4) = 8, then WCHAR[]
        const int headerSize = 8;
        var size = headerSize + 1024;
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(ptr, 0, unchecked((int)portIndex));

            if (!Native.DeviceIoControl(hubHandle, Native.IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME,
                    ptr, size, ptr, size, out _, IntPtr.Zero))
                return null;

            // USB_NODE_CONNECTION_DRIVERKEY_NAME: ULONG ConnectionIndex; ULONG ActualLength; WCHAR DriverKeyName[]
            var name = ReadWideStringAt(ptr, 8);
            return string.IsNullOrEmpty(name) ? null : name;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static string? GetProductString(IntPtr hubHandle, uint portIndex, byte productIndex)
    {
        if (productIndex == 0)
            return null;

        // USB_DESCRIPTOR_REQUEST + USB_STRING_DESCRIPTOR response
        var requestSize = 4 + 8; // ConnectionIndex + setup packet
        var bufferLen = 512;
        var ptr = Marshal.AllocHGlobal(requestSize + bufferLen);
        try
        {
            Marshal.WriteInt32(ptr, 0, unchecked((int)portIndex));
            // Setup packet: bmRequest=0x80 (in), bRequest=GET_DESCRIPTOR(6),
            // wValue=(0x03 string << 8) | index, wIndex=langid 0, wLength=bufferLen
            Marshal.WriteByte(ptr, 4, 0x80);
            Marshal.WriteByte(ptr, 5, 0x06);
            Marshal.WriteInt16(ptr, 6, (short)(0x0300 | productIndex));
            Marshal.WriteInt16(ptr, 8, 0);
            Marshal.WriteInt16(ptr, 10, (short)bufferLen);

            if (!Native.DeviceIoControl(hubHandle, IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION,
                    ptr, requestSize + bufferLen, ptr, requestSize + bufferLen,
                    out int returned, IntPtr.Zero))
                return null;

            // Response after the request header: BYTE bLength, bDescriptorType, WCHAR bString[]
            if (returned <= requestSize + 2)
                return null;
            return Marshal.PtrToStringUni(ptr + requestSize + 2, (returned - requestSize - 2) / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // ---------------- devnode enrichment (names, class, children, disabled state) ----------------

    private static void EnrichFromDevnode(UsbDeviceInfo device, string instanceId, string? product)
    {
        try
        {
            if (Native.CM_Locate_DevNodeW(out var devInst, instanceId, Native.CM_LOCATE_DEVNODE_NORMAL) != Native.CR_SUCCESS)
            {
                device.DisplayName = product ?? device.HardwareId;
                return;
            }

            device.DisplayName =
                Native.GetCmString(devInst, Native.CM_DRP_FRIENDLYNAME)
                ?? Native.GetCmString(devInst, Native.CM_DRP_DEVICEDESC)
                ?? product
                ?? device.HardwareId;
            device.Manufacturer = Native.GetCmString(devInst, Native.CM_DRP_MFG) ?? "";
            device.ClassName = Native.GetCmString(devInst, Native.CM_DRP_CLASS) ?? "";
            device.HasDisabledDevnode = DevicePowerService.IsDisabledDevInst(devInst);

            // Functional children carry the real class (XUSB\XINPUT, HID\VID_.., etc.)
            var sawHid = false;
            var child = 0u;
            if (Native.CM_Get_Child(out child, devInst, 0) == Native.CR_SUCCESS)
            {
                while (true)
                {
                    ClassifyChild(device, child, ref sawHid);
                    if (Native.CM_Get_Sibling(out var next, child, 0) != Native.CR_SUCCESS)
                        break;
                    child = next;
                }
            }

            device.GameRelevant = sawHid || device.ClassName.Equals("HIDClass", StringComparison.OrdinalIgnoreCase);

            // Prefer the child's friendlier name when the raw USB devnode is generic.
            if (!string.IsNullOrEmpty(device.ChildDisplayName))
                device.DisplayName = device.ChildDisplayName;
        }
        catch
        {
            device.DisplayName ??= device.HardwareId;
        }
    }

    private static void ClassifyChild(UsbDeviceInfo device, uint childDevInst, ref bool sawHid)
    {
        var id = Native.GetDeviceId(childDevInst);
        if (string.IsNullOrEmpty(id))
            return;

        if (id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase))
            sawHid = true;

        var cls = Native.GetCmString(childDevInst, Native.CM_DRP_CLASS);
        if (cls is not null && cls.Equals("HIDClass", StringComparison.OrdinalIgnoreCase))
            sawHid = true;

        var friendly = Native.GetCmString(childDevInst, Native.CM_DRP_FRIENDLYNAME);
        var desc = Native.GetCmString(childDevInst, Native.CM_DRP_DEVICEDESC);

        // The XINPUT child is the device name players recognize ("Xbox One Controller").
        if (!string.IsNullOrEmpty(friendly) &&
            (id.StartsWith("XUSB\\", StringComparison.OrdinalIgnoreCase)
             || (desc?.Contains("Controller", StringComparison.OrdinalIgnoreCase) ?? false)
             || (friendly.Contains("Controller", StringComparison.OrdinalIgnoreCase))))
        {
            device.ChildDisplayName = friendly;
        }
        else if (string.IsNullOrEmpty(device.ChildDisplayName) && !string.IsNullOrEmpty(desc)
                 && (id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase)
                     || id.StartsWith("XUSB\\", StringComparison.OrdinalIgnoreCase)))
        {
            device.ChildDisplayName = desc;
        }
    }

    // ---------------- helpers ----------------

    /// <summary>"\\?\USB#VID_045E&amp;PID_02FF#6&amp;123&amp;0#{guid}" → "USB\VID_045E&amp;PID_02FF\6&amp;123&amp;0"</summary>
    public static string? DevicePathToInstanceId(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
            return null;
        var s = devicePath.TrimStart('\\', '?');
        var hash = s.IndexOf('#');
        if (hash < 0)
            return null;
        var end = s.IndexOf("#{", hash, StringComparison.Ordinal);
        if (end < 0)
            return null;
        return s.Substring(hash + 1, end - hash - 1).Replace('#', '\\');
    }

    private static IEnumerable<string> EnumerateInterfacePaths(Guid classGuid)
    {
        var set = Native.SetupDiGetClassDevsW(ref classGuid, null, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            Diag.Log($"interfaces: SetupDiGetClassDevs failed, err={Marshal.GetLastWin32Error()}");
            yield break;
        }

        try
        {
            var index = 0;
            while (true)
            {
                var iface = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref classGuid, index, ref iface))
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err != 0 && err != 259 /* ERROR_NO_MORE_ITEMS */)
                        Diag.Log($"interfaces: enum failed at {index}, err={err}");
                    break;
                }
                index++;

                if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref iface, IntPtr.Zero, 0,
                        out var required, IntPtr.Zero))
                {
                    if (Marshal.GetLastWin32Error() != 122 /* ERROR_INSUFFICIENT_BUFFER */ || required <= 0)
                        continue;

                    var buf = Marshal.AllocHGlobal(required);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD cbSize(4); WCHAR DevicePath[]
                        // cbSize must equal the sizeof of the *struct as C sees it* = 8 on x64
                        // (4-byte cbSize + 4 bytes alignment before WCHAR[]), which is what
                        // SetupAPI validates against.
                        Marshal.WriteInt32(buf, 0, IntPtr.Size);
                        if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref iface, buf, required,
                                out _, IntPtr.Zero))
                        {
                            Diag.Log($"interfaces: detail call failed, err={Marshal.GetLastWin32Error()}");
                            continue;
                        }

                        // DevicePath starts right after the 4-byte cbSize field.
                        yield return Marshal.PtrToStringUni(buf + 4) ?? "";
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buf);
                    }
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string ReadWideStringAt(IntPtr basePtr, int byteOffset)
    {
        var s = Marshal.PtrToStringUni(basePtr + byteOffset);
        return s ?? "";
    }

    private static string ReadUnicodeAfterHeader(IntPtr ptr, int headerBytes)
    {
        var s = Marshal.PtrToStringUni(ptr + headerBytes);
        return s ?? "";
    }
}
