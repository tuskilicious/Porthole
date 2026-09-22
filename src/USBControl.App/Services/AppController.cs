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
    [NotifyPropertyChangedFor(nameof(StatusBannerText))]
    private string? activeProfile;

    // Summary band: counted from the full snapshot (not the filtered view).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBannerText))]
    private int deviceCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBannerText))]
    private int enabledCount;

    [ObservableProperty]
    private int disabledCount;

    [ObservableProperty]
    private int hubCount;

    /// <summary>Identity of the device whose power change is in flight (its tile shows the Busy chip).</summary>
    [ObservableProperty]
    private string? busyDeviceIdentity;

    /// <summary>"12 of 16 ports live", with " — profile 'X'" appended once a profile has been applied.</summary>
    public string StatusBannerText =>
        string.IsNullOrEmpty(ActiveProfile)
            ? $"{EnabledCount} of {DeviceCount} ports live"
            : $"{EnabledCount} of {DeviceCount} ports live — profile '{ActiveProfile}'";

    /// <summary>Raised after a rebuild when the previously toggled port re-appeared (keeps the editor pinned).</summary>
    public event Action<PortEntry>? PortFocused;

    public AppStore Store { get; }

    /// <summary>
    /// Asks the user a yes/no question (title, message). Wired by the UI at startup; while it is
    /// null (unit tests, headless use) risky actions proceed without asking.
    /// </summary>
    public Func<string, string, bool>? Confirm { get; set; }
    public ObservableCollection<HubGroupViewModel> Hubs { get; } = new();
    public ObservableCollection<PortViewModel> PanelPorts { get; } = new();

    /// <summary>Ports the user marked as physically on the case's front panel (see
    /// <see cref="TogglePortZone"/>) — shown in their own small fixed grid instead of
    /// the hub-grouped list or the free-form Panel Layout canvas.</summary>
    public ObservableCollection<PortViewModel> FrontPanelPorts { get; } = new();
    public ObservableCollection<Profile> ProfileList { get; } = new();

    public AppController(AppStore store, ITopologyService topology, IDevicePowerService power)
    {
        Store = store;
        _topology = topology;
        _power = power;
        _profiles = new ProfileEngine(power);

        SyncProfileList();
        _topology.Changed += OnTopologyChanged;
        _ = RefreshAsync(firstRun: true).ContinueWith(_ => RunOnUi(CheckInterruptedApply));
    }

    // ---------------- lockout safety ----------------

    private string ApplyFlagPath => Path.Combine(Store.RootDir, "apply-in-progress.flag");

    /// <summary>False when the user declined to disable a keyboard/mouse the guard flagged.</summary>
    private bool ConfirmDisable(TopologySnapshot snapshot, IEnumerable<string> instanceIds, IEnumerable<string>? enablingInstanceIds = null)
    {
        var risks = InputGuard.Assess(snapshot, instanceIds, enablingInstanceIds);
        return risks.Count == 0 || Confirm is null
            || Confirm(risks.Any(r => r.IsLastEnabled) ? "Disable your last keyboard or mouse?" : "Disable input device?",
                InputGuard.Describe(risks));
    }

    /// <summary>
    /// A profile apply that never finished (crash, power loss) leaves a marker file behind. On the
    /// next start, offer to undo whatever was left disabled.
    /// </summary>
    private void CheckInterruptedApply()
    {
        if (!File.Exists(ApplyFlagPath))
            return;
        TryDeleteFlag();
        if (Confirm?.Invoke("Interrupted profile apply",
                "Porthole closed while applying a profile, so some devices may have been left disabled.\n\nEnable every disabled device now?") == true)
            _ = EnableAllDisabledAsync();
    }

    private void TryDeleteFlag()
    {
        try { File.Delete(ApplyFlagPath); } catch { /* best effort */ }
    }

    /// <summary>Escape hatch: enables every device that is currently disabled.</summary>
    public async Task EnableAllDisabledAsync()
    {
        var targets = _snapshot.AllPorts
            .Where(p => p.Device is { IsHub: false } && p.State == PortState.Disabled)
            .Select(p => p.Device!)
            .ToList();
        if (targets.Count == 0)
        {
            StatusText = "Nothing is disabled.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusText = $"Enabling {targets.Count} disabled device{(targets.Count == 1 ? "" : "s")}…";
            var failed = await Task.Run(() =>
            {
                var errors = new List<string>();
                foreach (var d in targets)
                {
                    var (ok, error) = _power.SetEnabled(d.InstanceId, true);
                    if (ok)
                        Store.GetOrCreateDevice(d.Identity, d.InstanceId, d.DisplayName).LastKnownEnabled = true;
                    else
                        errors.Add($"{d.DisplayName}: {error}");
                }
                return errors;
            });
            Store.Save();
            await RefreshAsync();
            StatusText = failed.Count == 0
                ? $"Enabled {targets.Count} device{(targets.Count == 1 ? "" : "s")}."
                : $"Enabled {targets.Count - failed.Count} of {targets.Count}; failed: {string.Join("; ", failed.Take(3))}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public AppSettings Settings => Store.Data.Settings;

    // ---------------- refresh ----------------

    private void OnTopologyChanged()
    {
        // Fires from the topology watcher's own background thread, not the UI thread — RefreshAsync
        // (and the ObservableProperty sets at the top of RunRefreshCoreAsync) must start on the UI
        // thread like every other caller of RefreshAsync, or WPF's data-bound properties get touched
        // off-thread.
        Interlocked.Increment(ref _changeSeq);
        RunOnUi(() => _ = RefreshAsync());
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
                // Runs on every refresh (including the ~30s "last seen" metadata bump in
                // TopologyMerger), not just explicit user edits — keep the synchronous
                // serialize+write off the UI thread.
                await Task.Run(Store.Save);

                var devices = _snapshot.AllPorts.Count(p => p.Device is not null);
                var disabled = _snapshot.AllPorts.Count(p => p.State == PortState.Disabled);
                StatusText = $"Updated {DateTime.Now:HH:mm:ss} — {devices} devices, {disabled} disabled";
                DeviceCount = devices;
                DisabledCount = disabled;
                EnabledCount = devices - disabled;
                HubCount = _snapshot.Hubs.Count;

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

    /// <summary>The hub's friendly name from the full snapshot — unlike <see cref="Hubs"/>, this
    /// covers a hub even when every one of its ports is zoned to the front panel and it has
    /// nothing left to show in the grouped view.</summary>
    public string? FindHubDisplayName(string hubKey) =>
        _snapshot.Hubs.FirstOrDefault(h => h.HubKey == hubKey)?.DisplayName;

    // "port|identity" of every device on screen after the last rebuild; a tile whose device is
    // not in here is new and plays the appear animation.
    private HashSet<string> _shownDevices = new();

    private void RebuildCollections()
    {
        RunOnUi(() =>
        {
            Hubs.Clear();
            PanelPorts.Clear();
            FrontPanelPorts.Clear();
            var shownNow = new HashSet<string>();
            var hubIndex = 0;
            foreach (var hub in _snapshot.Hubs)
            {
                // Index is only consumed if this hub ends up with any visible ports (below) —
                // otherwise a hub emptied out entirely by front-panel zoning would still burn a
                // number, making the next visible hub's header skip one (e.g. "HUB 1" then "HUB 3").
                var vm = new HubGroupViewModel(this, hub, hubIndex);
                foreach (var port in hub.Ports.Where(ShouldShow))
                {
                    var deviceKey = port.Device is null ? null : $"{port.PortKey}|{port.Device.Identity}";
                    if (deviceKey is not null)
                        shownNow.Add(deviceKey);
                    var portVm = new PortViewModel(this, port,
                        appeared: deviceKey is not null && !_shownDevices.Contains(deviceKey));
                    if (Store.GetOrCreatePort(port.PortKey).Zone == "Front")
                    {
                        FrontPanelPorts.Add(portVm);
                    }
                    else
                    {
                        vm.Ports.Add(portVm);
                        PanelPorts.Add(portVm);
                    }
                }
                if (vm.Ports.Count > 0)
                {
                    Hubs.Add(vm);
                    hubIndex++;
                }
            }

            _shownDevices = shownNow;
            OnPropertyChanged(nameof(Hubs));

            if (_lastToggledIdentity is not null)
            {
                var again = _snapshot.AllPorts.FirstOrDefault(p =>
                    p.Device is not null &&
                    p.Device.Identity.Equals(_lastToggledIdentity, StringComparison.OrdinalIgnoreCase));
                _lastToggledIdentity = null; // one-shot: don't re-fire on every future, unrelated refresh
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

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var haystack = $"{meta.Label} {port.Device?.DisplayName} {port.Device?.HardwareId} {port.Device?.Serial} {port.Device?.InstanceId}";
            if (!haystack.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private string _searchText = "";

    /// <summary>Free-text sidebar search: filters every view (hub-grouped, panel layout,
    /// front panel) by port label, device name, VID/PID/serial or instance id.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            // Filtering never needs a fresh hardware walk — only which of the already-known
            // ports/hubs are shown changes. A full RefreshAsync() here would re-enumerate the
            // real USB tree on every keystroke while typing in the search box.
            RebuildCollections();
        }
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

        if (!enable && !ConfirmDisable(_snapshot, new[] { port.Device.InstanceId }))
        {
            StatusText = $"Left {port.Device.DisplayName} enabled.";
            await RefreshAsync(); // snaps the tile's switch back to its real state
            return;
        }

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

    /// <summary>Flips a port between the front-panel grid and the normal hub/panel-layout view.</summary>
    public void TogglePortZone(PortEntry port)
    {
        var meta = Store.GetOrCreatePort(port.PortKey);
        meta.Zone = meta.Zone == "Front" ? "" : "Front";
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
        // Captured once, before the confirm dialog: a hot-plug refresh landing while the modal
        // is open (WPF's dispatcher still pumps during a native MessageBox loop) must not let the
        // ops actually applied drift from the ones the user was warned about and approved.
        var snapshot = _snapshot;
        var ops = ProfileEngine.BuildOps(snapshot, profile);
        var disabling = ops.Where(o => !o.Enable).Select(o => o.InstanceId).ToList();
        var enabling = ops.Where(o => o.Enable).Select(o => o.InstanceId).ToList();
        if (!ConfirmDisable(snapshot, disabling, enabling))
        {
            StatusText = $"Profile '{profile.Name}' not applied.";
            return;
        }

        IsBusy = true;
        try
        {
            try { File.WriteAllText(ApplyFlagPath, DateTime.UtcNow.ToString("O")); } catch { /* best effort */ }
            // Created on the caller (UI) thread so reports marshal correctly.
            var progress = new Progress<string>(s => StatusText = s);
            var report = await Task.Run(() => _profiles.Apply(profile, snapshot, progress));
            Settings.LastProfile = profile.Name;
            Store.Save();
            ActiveProfile = profile.Name;
            TryDeleteFlag(); // finished: nothing to recover
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
