using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using USBControl.Core;

namespace USBControl.App.Services;

/// <summary>
/// Central state engine: owns the store and topology, exposes observable collections
/// for the UI, and mediates every toggle/rename/profile action. Enumeration and device
/// power work happen on worker threads; UI collection updates are marshaled to the UI thread.
/// </summary>
public sealed partial class AppController : ObservableObject, IDisposable
{
    private readonly ITopologyService _topology;
    private readonly IDevicePowerService _power;
    private readonly ProfileEngine _profiles;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private TopologySnapshot _snapshot = new();
    private long _changeSeq;
    private readonly object _refreshGate = new();
    private Task? _refreshTask;
    private string? _lastToggledIdentity;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = "Starting…";

    [ObservableProperty]
    private string? activeProfile;

    /// <summary>Identity of the device whose power change is in flight (its tile shows the Busy chip).</summary>
    [ObservableProperty]
    private string? busyDeviceIdentity;

    /// <summary>Raised after a rebuild when the previously toggled port re-appeared (keeps the editor pinned).</summary>
    public event Action<PortEntry>? PortFocused;

    public AppStore Store { get; }
    public ObservableCollection<HubGroupViewModel> Hubs { get; } = new();
    public ObservableCollection<PortViewModel> PanelPorts { get; } = new();
    public ObservableCollection<Profile> ProfileList { get; } = new();

    public AppController(AppStore store, ITopologyService topology, IDevicePowerService power)
    {
        Store = store;
        _topology = topology;
        _power = power;
        _profiles = new ProfileEngine(power);

        SyncProfileList();
        _topology.Changed += OnTopologyChanged;
        _ = RefreshAsync(firstRun: true);
    }

    public AppSettings Settings => Store.Data.Settings;

    // ---------------- refresh ----------------

    private void OnTopologyChanged()
    {
        Interlocked.Increment(ref _changeSeq);
        _ = RefreshAsync();
    }

    /// <summary>
    /// Refreshes the snapshot. Callers always await the most recent refresh: joining an
    /// in-flight one guarantees that by the time this returns, no older refresh can
    /// complete afterwards and stomp status/UI written on top of it.
    /// </summary>
    public Task RefreshAsync(bool firstRun = false)
    {
        lock (_refreshGate)
        {
            if (_refreshTask is { IsCompleted: false })
                return _refreshTask;
            _refreshTask = RunRefreshCoreAsync(firstRun);
            return _refreshTask;
        }
    }

    private async Task RunRefreshCoreAsync(bool firstRun)
    {
        IsBusy = true;
        try
        {
            while (true)
            {
                var startSeq = Interlocked.Read(ref _changeSeq);

                var snapshot = await Task.Run(() =>
                {
                    var s = _topology.Snapshot();
                    MergeStore(s);
                    return s;
                });

                _snapshot = snapshot;
                EnsurePanelLayout();
                RebuildCollections();
                Store.Save();

                var devices = _snapshot.AllPorts.Count(p => p.Device is not null);
                var disabled = _snapshot.AllPorts.Count(p => p.State == PortState.Disabled);
                StatusText = $"Updated {DateTime.Now:HH:mm:ss} — {devices} devices, {disabled} disabled";

                if (firstRun && !string.IsNullOrEmpty(Settings.LastProfile))
                {
                    ActiveProfile = Settings.LastProfile;
                    firstRun = false;
                }

                // If hardware changed while we enumerated, run one more pass so we
                // never present a stale tree as fresh.
                if (Interlocked.Read(ref _changeSeq) == startSeq)
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusText = "Enumeration failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void MergeStore(TopologySnapshot snapshot) => TopologyMerger.Merge(snapshot, Store);

    private void RebuildCollections()
    {
        RunOnUi(() =>
        {
            Hubs.Clear();
            PanelPorts.Clear();
            var hubIndex = 0;
            foreach (var hub in _snapshot.Hubs)
            {
                var vm = new HubGroupViewModel(this, hub, hubIndex++);
                foreach (var port in hub.Ports.Where(ShouldShow))
                {
                    var portVm = new PortViewModel(this, port);
                    vm.Ports.Add(portVm);
                    PanelPorts.Add(portVm);
                }
                Hubs.Add(vm);
            }

            OnPropertyChanged(nameof(Hubs));

            if (_lastToggledIdentity is not null)
            {
                var again = _snapshot.AllPorts.FirstOrDefault(p =>
                    p.Device is not null &&
                    p.Device.Identity.Equals(_lastToggledIdentity, StringComparison.OrdinalIgnoreCase));
                if (again is not null)
                    PortFocused?.Invoke(again);
            }
        });
    }

    private bool ShouldShow(PortEntry port)
    {
        var meta = Store.GetOrCreatePort(port.PortKey);
        if (meta.Hidden && !Settings.ShowHiddenPorts)
            return false;
        if (port.Device is null && !Settings.ShowEmptyPorts)
            return false;

        if (port.Device is { } dev && !Settings.ShowAllDevices)
        {
            // Controllers-first scope: hide non-HID hubs-peripheral noise, keep hubs and HID-ish devices.
            if (!dev.GameRelevant && !dev.IsHub && dev.ClassName is { Length: > 0 } cls
                && cls is "USB" or "SCSIAdapter" or "Net" or "DiskDrive" or "WPD")
                return false;
        }
        return true;
    }

    private void RunOnUi(Action action)
    {
        var ui = _ui;
        if (ui is not null && SynchronizationContext.Current != ui)
            ui.Post(_ => action(), null);
        else
            action();
    }

    // ---------------- device actions ----------------

    public async Task ToggleAsync(PortEntry port, bool enable)
    {
        if (port.Device is null)
            return;

        IsBusy = true;
        BusyDeviceIdentity = port.Device.Identity;
        StatusText = $"{(enable ? "Enabling" : "Disabling")} {port.Device.DisplayName}…";
        try
        {
            var result = await Task.Run(() => _power.SetEnabled(port.Device.InstanceId, enable));
            if (result.Ok)
            {
                _lastToggledIdentity = port.Device.Identity;
                var meta = Store.GetOrCreateDevice(port.Device.Identity, port.Device.InstanceId, port.Device.DisplayName);
                meta.LastKnownEnabled = enable;
                Store.Save();
            }
            await RefreshAsync();
            StatusText = result.Ok
                ? $"{port.Device.DisplayName} {(enable ? "enabled" : "disabled")}."
                : $"Failed: {result.Error}";
        }
        finally
        {
            BusyDeviceIdentity = null;
            IsBusy = false;
        }
    }

    public void RenameDevice(PortEntry port, string friendlyName)
    {
        if (port.Device is null)
            return;
        var meta = Store.GetOrCreateDevice(port.Device.Identity, port.Device.InstanceId, port.Device.DisplayName);
        meta.FriendlyName = friendlyName.Trim();
        port.Device.DisplayName = string.IsNullOrWhiteSpace(meta.FriendlyName)
            ? port.Device.DisplayName
            : meta.FriendlyName;
        Store.Save();
        _ = RefreshAsync();
    }

    public void RenamePort(PortEntry port, string label)
    {
        var meta = Store.GetOrCreatePort(port.PortKey);
        meta.Label = label.Trim();
        Store.Save();
    }

    public void TogglePortHidden(PortEntry port)
    {
        var meta = Store.GetOrCreatePort(port.PortKey);
        meta.Hidden = !meta.Hidden;
        Store.Save();
        _ = RefreshAsync();
    }

    public void SetDevicePhoto(PortEntry port, string sourceFile)
    {
        if (port.Device is null)
            return;
        var meta = Store.GetOrCreateDevice(port.Device.Identity, port.Device.InstanceId, port.Device.DisplayName);
        var old = meta.PhotoFile;
        meta.PhotoFile = Store.ImportPhoto(sourceFile);
        if (!string.Equals(old, meta.PhotoFile, StringComparison.OrdinalIgnoreCase))
            Store.DeletePhoto(old);
        Store.Save();
        _ = RefreshAsync();
    }

    public void ClearDevicePhoto(PortEntry port)
    {
        if (port.Device is null)
            return;
        var meta = Store.GetOrCreateDevice(port.Device.Identity, port.Device.InstanceId, port.Device.DisplayName);
        Store.DeletePhoto(meta.PhotoFile);
        meta.PhotoFile = null;
        Store.Save();
        _ = RefreshAsync();
    }

    public string? PhotoPathFor(string? photoFile) =>
        string.IsNullOrEmpty(photoFile) ? null : Path.Combine(Store.PhotosDir, photoFile);

    // ---------------- view toggles ----------------

    // Observable wrappers so toolbar ToggleButtons can two-way-bind (the Settings
    // checkboxes bind the raw Settings object; both paths converge on ApplyShowFlags).

    public bool ShowAllDevices
    {
        get => Settings.ShowAllDevices;
        set { Settings.ShowAllDevices = value; ApplyShowFlags(); }
    }

    public bool ShowEmptyPorts
    {
        get => Settings.ShowEmptyPorts;
        set { Settings.ShowEmptyPorts = value; ApplyShowFlags(); }
    }

    public bool ShowHiddenPorts
    {
        get => Settings.ShowHiddenPorts;
        set { Settings.ShowHiddenPorts = value; ApplyShowFlags(); }
    }

    private void ApplyShowFlags()
    {
        OnPropertyChanged(nameof(ShowAllDevices));
        OnPropertyChanged(nameof(ShowEmptyPorts));
        OnPropertyChanged(nameof(ShowHiddenPorts));
        Store.Save();
        _ = RefreshAsync();
    }

    public void SetShowAll(bool value)
    {
        Settings.ShowAllDevices = value;
        ApplyShowFlags();
    }

    public void SetShowEmpty(bool value)
    {
        Settings.ShowEmptyPorts = value;
        ApplyShowFlags();
    }

    public void SetShowHidden(bool value)
    {
        Settings.ShowHiddenPorts = value;
        ApplyShowFlags();
    }

    /// <summary>Re-applies current settings after the settings dialog saved them.</summary>
    public void RefreshFromSettings() => _ = RefreshAsync();

    /// <summary>Switches the neon accent palette live and persists the choice.</summary>
    public void SetAccent(string name)
    {
        if (Settings.Accent.Equals(name, StringComparison.OrdinalIgnoreCase))
            return;
        Settings.Accent = name;
        Store.Save();
        Accents.Apply(name);
    }

    // ---------------- panel layout ----------------

    /// <summary>Free-form "rear panel" view is on when the setting is on (toolbar toggle).</summary>
    public bool IsPanelMode
    {
        get => Settings.UsePanelLayout;
        set
        {
            if (Settings.UsePanelLayout == value)
                return;
            Settings.UsePanelLayout = value;
            Store.Save();
            OnPropertyChanged();
        }
    }

    public (double X, double Y) GetPanelPos(string portKey)
    {
        var l = Store.Data.PanelLayout.FirstOrDefault(l =>
            l.PortKey.Equals(portKey, StringComparison.OrdinalIgnoreCase));
        return l is null ? (PanelLayoutMath.Margin, PanelLayoutMath.Margin) : (l.X, l.Y);
    }

    /// <summary>Called by the drag behavior when a tile is dropped; snaps, persists, updates the VM.</summary>
    public void MovePortTo(PortViewModel vm, double x, double y)
    {
        var nx = PanelLayoutMath.SnapX(x);
        var ny = PanelLayoutMath.SnapY(y);

        var layout = Store.Data.PanelLayout.FirstOrDefault(l =>
            l.PortKey.Equals(vm.Entry.PortKey, StringComparison.OrdinalIgnoreCase));
        if (layout is null)
        {
            layout = new PortLayout { PortKey = vm.Entry.PortKey };
            Store.Data.PanelLayout.Add(layout);
        }

        if (Math.Abs(layout.X - nx) < 0.5 && Math.Abs(layout.Y - ny) < 0.5)
            return;
        layout.X = nx;
        layout.Y = ny;
        Store.Save();
        vm.SetPosition(nx, ny);
    }

    public void ResetPanelLayout()
    {
        Store.Data.PanelLayout.Clear();
        Store.Save();
        _ = RefreshAsync();
    }

    /// <summary>Assigns canvas positions to ports the user has never placed (auto-layout, stable order).</summary>
    private void EnsurePanelLayout()
    {
        var layout = Store.Data.PanelLayout;
        var known = new HashSet<string>(
            layout.Select(l => l.PortKey), StringComparer.OrdinalIgnoreCase);

        var missing = _snapshot.AllPorts
            .Where(p => !known.Contains(p.PortKey))
            .Select(p => p.PortKey)
            .ToList();
        if (missing.Count == 0)
            return;

        var occupied = layout
            .Where(l => known.Contains(l.PortKey))
            .Select(l => (l.X, l.Y))
            .ToList();

        foreach (var (key, pos) in PanelLayoutMath.AutoPositions(missing, occupied))
            layout.Add(new PortLayout { PortKey = key, X = pos.X, Y = pos.Y });
    }

    // ---------------- profiles ----------------

    public Profile CaptureProfile(string name)
    {
        var profile = ProfileEngine.Capture(_snapshot, name);

        Store.Data.Profiles.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Store.Data.Profiles.Add(profile);
        Store.Data.Settings.LastProfile = name;
        Store.Save();
        SyncProfileList();
        ActiveProfile = name;
        return profile;
    }

    public async Task ApplyProfileAsync(Profile profile)
    {
        IsBusy = true;
        try
        {
            // Created on the caller (UI) thread so reports marshal correctly.
            var progress = new Progress<string>(s => StatusText = s);
            var snapshot = _snapshot; // stable reference for this batch
            var report = await Task.Run(() => _profiles.Apply(profile, snapshot, progress));
            Settings.LastProfile = profile.Name;
            Store.Save();
            ActiveProfile = profile.Name;
            await RefreshAsync();
            StatusText = report.Success
                ? $"Profile '{profile.Name}' applied ({report.Enabled} enabled, {report.Disabled} disabled)."
                : $"Profile '{profile.Name}' applied with errors: {string.Join("; ", report.Errors.Take(3))}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void DeleteProfile(Profile profile)
    {
        Store.Data.Profiles.Remove(profile);
        if (Settings.LastProfile?.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) == true)
            Settings.LastProfile = null;
        Store.Save();
        SyncProfileList();
    }

    private void SyncProfileList()
    {
        RunOnUi(() =>
        {
            ProfileList.Clear();
            foreach (var p in Store.Data.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                ProfileList.Add(p);
        });
    }

    public void Dispose() => (_topology as IDisposable)?.Dispose();
}
