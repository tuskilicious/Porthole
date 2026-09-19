using System.Text.RegularExpressions;

namespace USBControl.Core;

public static partial class DeviceIdentity
{
    [GeneratedRegex(@"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}", RegexOptions.IgnoreCase)]
    private static partial Regex VidPidRegex();

    /// <summary>Parses "USB\VID_045E&amp;PID_0B12\6&amp;1234&amp;0&amp;2" into (VID_xxxx&amp;PID_yyyy, serial-or-null).</summary>
    public static (string? VidPid, string? Serial) Parse(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return (null, null);

        var segments = instanceId.Split('\\');
        var hwSegment = segments.Length >= 2 ? segments[^2] : segments[0];
        var tail = segments[^1];

        var m = VidPidRegex().Match(hwSegment);
        if (!m.Success)
            m = VidPidRegex().Match(instanceId);
        if (!m.Success)
            return (null, null);

        // A device with a serial number has it as the last segment; location-based
        // tails contain '&' (e.g. "6&2f3827&0&2").
        string? serial = tail.Contains('&') || segments.Length < 3 ? null : tail;

        return (m.Value.ToUpperInvariant(), serial);
    }

    /// <summary>Stable identity string: VID:PID:SERIAL when a serial exists, else VID:PID.</summary>
    public static string Build(string instanceId)
    {
        var (vidPid, serial) = Parse(instanceId);
        if (vidPid is null)
            return instanceId; // fall back to instance id itself
        return serial is null ? vidPid : $"{vidPid}:{serial}";
    }

    /// <summary>
    /// Score how well a live instance id matches stored metadata:
    /// 3 = same instance, 2 = same full identity (vid/pid/serial), 1 = same vid/pid only, 0 = no match.
    /// </summary>
    public static int MatchScore(string candidateInstanceId, string storedIdentity, string? storedInstanceId = null)
    {
        if (!string.IsNullOrEmpty(storedInstanceId) &&
            string.Equals(candidateInstanceId, storedInstanceId, StringComparison.OrdinalIgnoreCase))
            return 3;

        var (candVidPid, candSerial) = Parse(candidateInstanceId);

        string storedVidPid;
        string? storedSerial;
        if (storedIdentity is null || candVidPid is null)
            return 0;
        var colon = storedIdentity.IndexOf(':'); // vid/pid never contains ':'
        if (colon > 0)
        {
            storedVidPid = storedIdentity.Substring(0, colon);
            storedSerial = storedIdentity.Substring(colon + 1);
        }
        else
        {
            storedVidPid = storedIdentity;
            storedSerial = null;
        }

        if (!string.Equals(candVidPid, storedVidPid, StringComparison.OrdinalIgnoreCase))
            return 0;

        return candSerial is not null && storedSerial is not null
            ? (string.Equals(candSerial, storedSerial, StringComparison.OrdinalIgnoreCase) ? 2 : 0)
            : 1;
    }
}
