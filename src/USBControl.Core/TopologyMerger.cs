namespace USBControl.Core;

/// <summary>
/// Pure store/snapshot merging: stamps live state into device metadata and applies
/// user renames on top of hardware names. Extracted from the UI controller so the
/// identity re-attachment rules can be tested without WPF.
/// </summary>
public static class TopologyMerger
{
    /// <summary>Updates metadata for every seen device and applies stored friendly names to the snapshot.</summary>
    public static void Merge(TopologySnapshot snapshot, AppStore store)
    {
        foreach (var port in snapshot.AllPorts)
        {
            if (port.Device is null)
                continue;

            var meta = store.GetOrCreateDevice(port.Device.Identity, port.Device.InstanceId, port.Device.DisplayName);

            // 30s granularity: avoids touching the store (and re-serializing it) on
            // every watcher refresh while staying accurate enough for "last seen".
            var now = DateTime.UtcNow;
            if (now - meta.LastSeenUtc > TimeSpan.FromSeconds(30))
                meta.LastSeenUtc = now;
            meta.LastKnownEnabled = port.State != PortState.Disabled;

            if (!string.IsNullOrWhiteSpace(meta.FriendlyName))
                port.Device.DisplayName = meta.FriendlyName;
        }
    }
}
