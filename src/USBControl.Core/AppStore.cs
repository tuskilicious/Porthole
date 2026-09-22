using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace USBControl.Core;

public sealed class AppStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string RootDir { get; }
    public string PhotosDir { get; }
    private string StorePath => Path.Combine(RootDir, "store.json");

    public AppData Data { get; private set; } = new();

    public AppStore(string? rootDir = null)
    {
        RootDir = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "USBControl");
        PhotosDir = Path.Combine(RootDir, "Photos");
    }

    public void Load()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(PhotosDir);

        if (!File.Exists(StorePath))
            return;

        if (TryReadStore(out var data))
        {
            Data = data;
            return;
        }

        // A file that's briefly locked (e.g. an antivirus scan right after the previous save)
        // fails to read exactly like a genuinely corrupt one — but treating that the same as
        // "no saved data" here means the very next Save() permanently overwrites a perfectly
        // good store.json with an empty one. One short retry covers the common transient case
        // without changing behavior for an actually-corrupt file (it just fails again).
        Thread.Sleep(250);
        Data = TryReadStore(out data) ? data : new AppData();
    }

    private bool TryReadStore(out AppData data)
    {
        try
        {
            var json = File.ReadAllText(StorePath);
            data = JsonSerializer.Deserialize<AppData>(json, JsonOptions) ?? new AppData();
            data.Devices ??= new(StringComparer.OrdinalIgnoreCase);
            data.Ports ??= new(StringComparer.OrdinalIgnoreCase);
            data.Profiles ??= new();
            data.Settings ??= new();
            return true;
        }
        catch
        {
            data = new AppData();
            return false;
        }
    }

    private readonly object _saveLock = new();
    private string? _lastSerialized;

    public void Save()
    {
        // Concurrent saves (watcher-triggered refresh + user action) must not collide
        // on the shared .tmp path, and unchanged data must not hit the disk at all.
        lock (_saveLock)
        {
            var json = JsonSerializer.Serialize(Data, JsonOptions);
            if (json == _lastSerialized)
                return;

            Directory.CreateDirectory(RootDir);
            var tmp = StorePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(StorePath))
                File.Replace(tmp, StorePath, null);
            else
                File.Move(tmp, StorePath);
            _lastSerialized = json;
        }
    }

    /// <summary>Copies a picked image into the Photos dir and returns the file name.</summary>
    public string ImportPhoto(string sourcePath)
    {
        Directory.CreateDirectory(PhotosDir);
        var ext = Path.GetExtension(sourcePath);
        var name = $"{Guid.NewGuid():N}{ext}";
        File.Copy(sourcePath, Path.Combine(PhotosDir, name), overwrite: true);
        return name;
    }

    public void DeletePhoto(string? photoFile)
    {
        if (string.IsNullOrEmpty(photoFile))
            return;
        try
        {
            var full = Path.Combine(PhotosDir, photoFile);
            if (File.Exists(full))
                File.Delete(full);
        }
        catch
        {
            // best effort
        }
    }

    public DeviceMeta GetOrCreateDevice(string identity, string instanceId, string displayName)
    {
        if (!Data.Devices.TryGetValue(identity, out var meta))
        {
            meta = new DeviceMeta { Identity = identity, InstanceId = instanceId };
            Data.Devices[identity] = meta;
        }
        if (string.IsNullOrEmpty(meta.InstanceId))
            meta.InstanceId = instanceId;
        return meta;
    }

    public PortMeta GetOrCreatePort(string portKey)
    {
        if (!Data.Ports.TryGetValue(portKey, out var meta))
        {
            meta = new PortMeta { PortKey = portKey };
            Data.Ports[portKey] = meta;
        }
        return meta;
    }
}
