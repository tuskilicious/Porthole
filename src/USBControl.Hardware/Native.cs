using System.Runtime.InteropServices;
using System.Text;

namespace USBControl.Hardware;

#pragma warning disable CA2101 // Marshal strings for P/Invoke

internal static class Native
{
    // ---------- CTL_CODE helpers (usbioctl.h / usbiodef.h) ----------
    private const uint FILE_DEVICE_UNKNOWN = 0x00000022;
    private const uint METHOD_BUFFERED = 0;
    private const uint FILE_ANY_ACCESS = 0;

    private static uint UsbCtl(uint function) =>
        (FILE_DEVICE_UNKNOWN << 16) | (FILE_ANY_ACCESS << 14) | (function << 2) | METHOD_BUFFERED;

    // Function codes from usbiodef.h
    private const uint USB_GET_NODE_INFORMATION = 258;
    private const uint USB_GET_NODE_CONNECTION_INFORMATION_EX = 274;
    private const uint USB_GET_NODE_CONNECTION_NAME = 261;
    private const uint USB_GET_NODE_CONNECTION_DRIVERKEY_NAME = 264;
    private const uint HCD_GET_ROOT_HUB_NAME = 258;

    public static readonly uint IOCTL_USB_GET_NODE_INFORMATION = UsbCtl(USB_GET_NODE_INFORMATION);
    public static readonly uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = UsbCtl(USB_GET_NODE_CONNECTION_INFORMATION_EX);
    public static readonly uint IOCTL_USB_GET_NODE_CONNECTION_NAME = UsbCtl(USB_GET_NODE_CONNECTION_NAME);
    public static readonly uint IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME = UsbCtl(USB_GET_NODE_CONNECTION_DRIVERKEY_NAME);
    public static readonly uint IOCTL_USB_GET_ROOT_HUB_NAME = UsbCtl(HCD_GET_ROOT_HUB_NAME);

    // ---------- GUIDs (usbiodef.h) ----------
    public static readonly Guid GuidDevInterfaceUsbHub = new("F18A0E88-C30C-11D0-8815-00A0C906BED8");
    public static readonly Guid GuidDevInterfaceUsbDevice = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    public static readonly Guid GuidDevInterfaceUsbHostController = new("3ABF6F2D-71C4-462A-8A92-1E6861E6AF27");

    // ---------- DEVPROPKEYs (devpkey.h) ----------
    public static readonly DEVPROPKEY Device_HardwareIds = Key(0xa45c254e, 0xdf1c, 0x4efd, 0x802067d146a850e0, 3);
    public static readonly DEVPROPKEY Device_Class = Key(0xa45c254e, 0xdf1c, 0x4efd, 0x802067d146a850e0, 9);
    public static readonly DEVPROPKEY Device_FriendlyName = Key(0xa45c254e, 0xdf1c, 0x4efd, 0x802067d146a850e0, 14);
    public static readonly DEVPROPKEY Device_Address = Key(0xa45c254e, 0xdf1c, 0x4efd, 0x802067d146a850e0, 30);
    public static readonly DEVPROPKEY Device_Driver = Key(0xa45c254e, 0xdf1c, 0x4efd, 0x802067d146a850e0, 11);
    public static readonly DEVPROPKEY Device_BusReportedDeviceDesc = Key(0x540b947e, 0x8b40, 0x45bc, 0xa8a26a0b894cbda2, 4);
    public static readonly DEVPROPKEY Device_Children = Key(0x4340a6c5, 0x93fa, 0x4706, 0x972c7b648008a5a7, 9);
    public static readonly DEVPROPKEY Device_DevNodeStatus = Key(0x4340a6c5, 0x93fa, 0x4706, 0x972c7b648008a5a7, 2);
    public static readonly DEVPROPKEY Device_ProblemCode = Key(0x4340a6c5, 0x93fa, 0x4706, 0x972c7b648008a5a7, 3);

    private static DEVPROPKEY Key(uint a, ushort b, ushort c, ulong g8, uint pid)
    {
        var guid = new Guid((int)a, (short)b, (short)c,
            (byte)g8, (byte)(g8 >> 8), (byte)(g8 >> 16), (byte)(g8 >> 24),
            (byte)(g8 >> 32), (byte)(g8 >> 40), (byte)(g8 >> 48), (byte)(g8 >> 56));
        return new DEVPROPKEY { Fmtid = guid, Pid = pid };
    }

    // Property types (devpropdef.h)
    public const uint DEVPROP_TYPE_STRING = 0x00000012;
    public const uint DEVPROP_TYPE_STRING_LIST = 0x00000013;
    public const uint DEVPROP_TYPE_UINT32 = 0x00000007;
    public const uint DEVPROP_TYPE_INT32 = 0x00000006;
    public const uint DEVPROP_TYPE_BOOLEAN = 0x00008003;

    // ---------- SetupAPI / CfgMgr32 constants ----------
    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_ALLCLASSES = 0x00000004;
    public const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    public const uint DICS_ENABLE = 0x00000001;
    public const uint DICS_DISABLE = 0x00000002;
    public const uint DICS_FLAG_GLOBAL = 0x00000001;
    public const uint DICS_FLAG_CONFIGSPECIFIC = 0x00000002;
    public const uint DIF_PROPERTYCHANGE = 0x00000012;

    public const uint DN_HAS_PROBLEM = 0x00000400;
    public const uint CM_PROB_DISABLED = 22;
    public const uint CM_PROB_HARDWARE_DISABLED = 29;
    public const uint CM_PROB_DISABLED_SERVICE = 56;

    public const int CR_SUCCESS = 0;
    public const int CR_BUFFER_SMALL = 0x1A;
    public const uint CM_DISABLE_PERSISTENT = 0x00000001;
    public const uint CM_LOCATE_DEVNODE_NORMAL = 0;

    // CM_DRP_* registry property codes (cfgmgr32.h)
    public const uint CM_DRP_DEVICEDESC = 0x00000001;
    public const uint CM_DRP_HARDWAREID = 0x00000002;
    public const uint CM_DRP_DRIVER = 0x0000000A; // the driver key, "{class-guid}\NNNN"
    public const uint CM_DRP_CLASS = 0x00000008;
    public const uint CM_DRP_MFG = 0x0000000C;
    public const uint CM_DRP_FRIENDLYNAME = 0x0000000D;
    public const uint CM_DRP_ENUMERATOR_NAME = 0x00000017;

    // ---------- Structs ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid Fmtid;
        public uint Pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_CLASSINSTALL_HEADER
    {
        public int cbSize;
        public uint InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct USB_DEVICE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public ushort bcdUSB;
        public byte bDeviceClass;
        public byte bDeviceSubClass;
        public byte bDeviceProtocol;
        public byte bMaxPacketSize0;
        public ushort idVendor;
        public ushort idProduct;
        public ushort bcdDevice;
        public byte iManufacturer;
        public byte iProduct;
        public byte iSerialNumber;
        public byte bNumConfigurations;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct USB_NODE_CONNECTION_INFORMATION_EX
    {
        public uint ConnectionIndex;
        public USB_DEVICE_DESCRIPTOR DeviceDescriptor;
        public byte CurrentConfigurationValue;
        public byte Speed;
        public byte DeviceIsHub;   // BOOLEAN (1 byte)
        public ushort DeviceAddress;
        public uint NumberOfOpenPipes;
        public uint ConnectionStatus;
        // PipeList[ANYSIZE_ARRAY] follows in native memory; we only use the fixed part.
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct USB_NODE_INFORMATION
    {
        public uint NodeType;      // USB_HUB_NODE: UsbHub = 0, UsbMIParent = 1

        // The union (HubInformation | MiParentInformation) starts right here at
        // offset 4 — the hub descriptor OVERLAPS it; there is no extra 4-byte field.
        // (A phantom ULONG here used to shift every descriptor read by 4 bytes, so
        // bNumberOfPorts read a zero byte and every hub looked port-less.)
        public byte HubDescriptor_bDescriptorLength;
        public byte HubDescriptor_bDescriptorType;
        public byte HubDescriptor_bNumberOfPorts;
        public ushort HubDescriptor_wHubCharacteristics;
        public byte HubDescriptor_bPowerOnToPowerGood;
        public byte HubDescriptor_bHubControlCurrent;
        public fixed byte HubDescriptor_bRemoveAndPowerMask[64];
        public byte HubIsBusPowered;
    }

    // ---------- Kernel32 ----------
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const int OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string fileName, uint desiredAccess, int shareMode,
        IntPtr securityAttributes, int creationDisposition, int flagsAndAttributes, IntPtr templateFile);

    public static IntPtr OpenDevice(string path) =>
        CreateFileW(path, GENERIC_READ | GENERIC_WRITE, 3 /*read|write share*/,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(IntPtr hDevice, uint ioControlCode,
        IntPtr inBuffer, int inBufferSize, IntPtr outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandleW(string? moduleName);

    // ---------- SetupAPI ----------
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator,
        IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuidNull, string? enumerator,
        IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, int memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet,
        IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        int detailBufferSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        int detailBufferSize, out int requiredSize, out SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiOpenDeviceInfoW(IntPtr deviceInfoSet, string deviceInstanceId,
        IntPtr hwndParent, uint openFlags, out SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiGetDevicePropertyW(IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData, ref DEVPROPKEY propertyKey, out uint propertyType,
        byte[]? propertyBuffer, int propertyBufferSize, out int requiredSize, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiSetClassInstallParamsW(IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData, ref SP_PROPCHANGE_PARAMS classInstallParams,
        int classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData);

    // ---------- CfgMgr32 ----------
    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber,
        uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Device_ID_Size(out uint size, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int bufferLen, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Enable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_DevNode_Registry_PropertyW(uint devInst, uint property,
        out uint regDataType, StringBuilder? buffer, ref uint bufferLength, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Child(out uint child, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Sibling(out uint sibling, uint devInst, uint flags);

    public static string GetDeviceId(uint devInst)
    {
        if (CM_Get_Device_ID_Size(out var size, devInst, 0) != CR_SUCCESS || size == 0)
            return "";
        var sb = new StringBuilder((int)size + 1);
        return CM_Get_Device_IDW(devInst, sb, sb.Capacity, 0) == CR_SUCCESS ? sb.ToString() : "";
    }

    /// <summary>Reads a CM registry string property (two-call pattern).</summary>
    public static string? GetCmString(uint devInst, uint property)
    {
        uint len = 0;
        var cr = CM_Get_DevNode_Registry_PropertyW(devInst, property, out _, (StringBuilder?)null, ref len, 0);
        // The sizing call reports the needed length with CR_BUFFER_SMALL, not CR_SUCCESS.
        if ((cr != CR_SUCCESS && cr != CR_BUFFER_SMALL) || len == 0)
            return null;
        var sb = new StringBuilder((int)len);
        return CM_Get_DevNode_Registry_PropertyW(devInst, property, out _, sb, ref len, 0) == CR_SUCCESS
            ? sb.ToString().TrimEnd('\0')
            : null;
    }

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_DevNode_Registry_PropertyW(uint devInst, uint property,
        out uint regDataType, byte[]? buffer, ref uint bufferLength, uint flags);

    /// <summary>Reads a CM DWORD registry property (two-call pattern).</summary>
    public static uint? GetCmDword(uint devInst, uint property)
    {
        uint len = 0;
        var cr = CM_Get_DevNode_Registry_PropertyW(devInst, property, out _, (byte[]?)null, ref len, 0);
        if ((cr != CR_SUCCESS && cr != CR_BUFFER_SMALL) || len != 4)
            return null;
        var buf = new byte[4];
        if (CM_Get_DevNode_Registry_PropertyW(devInst, property, out _, buf, ref len, 0) != CR_SUCCESS)
            return null;
        return BitConverter.ToUInt32(buf, 0);
    }

    // ---------- User32 (device watcher) ----------
    public delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSW
    {
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    public const int HWND_MESSAGE = -3;
    public const int WM_DEVICECHANGE = 0x0219;
    public const int WM_CLOSE = 0x0010;
    public const int DBT_DEVNODES_CHANGED = 0x0007;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

#pragma warning restore CA2101
}
