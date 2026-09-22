using System.Runtime.InteropServices;
using USBControl.Core;

namespace USBControl.Hardware;

/// <summary>
/// Enable/disable of devnodes via CfgMgr32 (CM_Enable/Disable_DevNode with
/// CM_DISABLE_PERSISTENT — the same mechanism Device Manager uses, so the state
/// persists across reboots and replugs), with a devcon-style SetupAPI fallback.
/// </summary>
public sealed class DevicePowerService : IDevicePowerService
{
    public (bool Ok, string? Error) SetEnabled(string instanceId, bool enable)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return (false, "no device instance id");

        var cr = Native.CM_Locate_DevNodeW(out var devInst, instanceId, Native.CM_LOCATE_DEVNODE_NORMAL);
        if (cr != Native.CR_SUCCESS)
        {
            // Device may be currently absent (disconnected); nothing to do.
            return (false, $"device node not found (CfgMgr32 0x{cr:X})");
        }

        cr = enable
            ? Native.CM_Enable_DevNode(devInst, 0)
            : Native.CM_Disable_DevNode(devInst, Native.CM_DISABLE_PERSISTENT);

        if (cr == Native.CR_SUCCESS)
        {
            // Give the PnP manager a beat so re-enumeration settles before callers re-snapshot.
            Thread.Sleep(enable ? 300 : 150);
            return (true, null);
        }

        var fallback = SetEnabledViaSetupApi(instanceId, enable);
        if (fallback.Ok)
            return fallback;

        return (false, $"CfgMgr32 0x{cr:X}{(fallback.Error is null ? "" : $"; {fallback.Error}")}");
    }

    public bool IsDisabled(string instanceId)
    {
        if (Native.CM_Locate_DevNodeW(out var devInst, instanceId, Native.CM_LOCATE_DEVNODE_NORMAL) != Native.CR_SUCCESS)
            return false;
        return IsDisabledDevInst(devInst);
    }

    internal static bool IsDisabledDevInst(uint devInst)
    {
        if (Native.CM_Get_DevNode_Status(out var status, out var problem, devInst, 0) != Native.CR_SUCCESS)
            return false;
        return (status & Native.DN_HAS_PROBLEM) != 0
            && problem is Native.CM_PROB_DISABLED
                or Native.CM_PROB_HARDWARE_DISABLED
                or Native.CM_PROB_DISABLED_SERVICE;
    }

    /// <summary>devcon-style fallback: DIF_PROPERTYCHANGE with DICS_ENABLE/DISABLE.</summary>
    private static (bool Ok, string? Error) SetEnabledViaSetupApi(string instanceId, bool enable)
    {
        var emptyGuid = Guid.Empty;
        var set = Native.SetupDiGetClassDevsW(ref emptyGuid, null, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_ALLCLASSES);
        if (set == new IntPtr(-1) || set == IntPtr.Zero)
            return (false, $"SetupDiGetClassDevs failed (Win32 0x{Marshal.GetLastWin32Error():X})");

        try
        {
            if (!Native.SetupDiOpenDeviceInfoW(set, instanceId, IntPtr.Zero, 0, out var devInfo))
                return (false, $"SetupDiOpenDeviceInfo failed (Win32 0x{Marshal.GetLastWin32Error():X})");

            foreach (var scope in new[] { Native.DICS_FLAG_GLOBAL, Native.DICS_FLAG_CONFIGSPECIFIC })
            {
                var pcp = new Native.SP_PROPCHANGE_PARAMS
                {
                    ClassInstallHeader = new Native.SP_CLASSINSTALL_HEADER
                    {
                        cbSize = Marshal.SizeOf<Native.SP_CLASSINSTALL_HEADER>(),
                        InstallFunction = Native.DIF_PROPERTYCHANGE,
                    },
                    StateChange = enable ? Native.DICS_ENABLE : Native.DICS_DISABLE,
                    Scope = scope,
                    HwProfile = 0,
                };

                Native.SetupDiSetClassInstallParamsW(set, ref devInfo, ref pcp, Marshal.SizeOf<Native.SP_PROPCHANGE_PARAMS>());
                Native.SetupDiCallClassInstaller(Native.DIF_PROPERTYCHANGE, set, ref devInfo);
            }

            // Success means the post-change disabled-state is the opposite of "enable"
            // (enabled -> not disabled, disabled -> disabled).
            if (IsDisabledDevInst(devInfo.DevInst) != enable)
                return (true, null);

            return (false, $"state did not change (Win32 0x{Marshal.GetLastWin32Error():X})");
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }
    }
}
