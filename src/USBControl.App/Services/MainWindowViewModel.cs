using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using USBControl.Core;

namespace USBControl.App.Services;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private PortViewModel? _selectedPort;
    private Profile? _selectedProfile;

    public AppController Controller { get; }

    public Func<string, string?>? ProfileNamePrompt { get; set; }

    /// <summary>Optional short description shown under the profile's name in the sidebar
    /// (e.g. "Stick · Throttle · Rudder · 6…"). Skipped silently if left blank.</summary>
    public Func<string, string?>? ProfileNotePrompt { get; set; }

    public ObservableCollection<Profile> ProfileList => Controller.ProfileList;

    public string MachineName => Environment.MachineName;

    public PortViewModel? SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (_selectedPort is not null)
                _selectedPort.IsSelected = false;
            _selectedPort = value;
            if (value is not null)
                value.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    private bool _isEditorOpen;

    /// <summary>The 340px device editor is expanded; false (the default, until a port is picked) leaves just the rail.</summary>
    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        set
        {
            if (_isEditorOpen == value) return;
            _isEditorOpen = value;
            OnPropertyChanged();
        }
    }

    public bool HasSelection => SelectedPort is not null;

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy => Controller.IsBusy;
    public string StatusText => Controller.StatusText;
    public bool HasNoHubs => Controller.Hubs.Count == 0;

    public ICommand RefreshCommand { get; }
    public ICommand ToggleShowAllCommand { get; }
    public ICommand ToggleShowEmptyCommand { get; }
    public ICommand ToggleShowHiddenCommand { get; }
    public ICommand ApplyProfileCommand { get; }
    public ICommand CaptureProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand HidePortCommand { get; }
    public ICommand ResetPanelLayoutCommand { get; }
    public ICommand ToggleEditorCommand { get; }
    public ICommand EnableAllCommand { get; }

    public MainWindowViewModel(AppController controller)
    {
        Controller = controller;

        RefreshCommand = new RelayCommand(_ => _ = Controller.RefreshAsync());
        ToggleShowAllCommand = new RelayCommand(_ => Controller.SetShowAll(!Controller.Settings.ShowAllDevices));
        ToggleShowEmptyCommand = new RelayCommand(_ => Controller.SetShowEmpty(!Controller.Settings.ShowEmptyPorts));
        ToggleShowHiddenCommand = new RelayCommand(_ => Controller.SetShowHidden(!Controller.Settings.ShowHiddenPorts));
        ApplyProfileCommand = new RelayCommand(_ => _ = ApplySelectedProfileAsync(), _ => SelectedProfile is not null);
        CaptureProfileCommand = new RelayCommand(_ => CaptureCurrent());
        DeleteProfileCommand = new RelayCommand(_ => { if (SelectedProfile is { } p) Controller.DeleteProfile(p); },
            _ => SelectedProfile is not null);
        OpenSettingsCommand = new RelayCommand(_ => Services.SettingsWindow.Show(Controller));
        HidePortCommand = new RelayCommand(o => { if (o is PortViewModel vm) Controller.TogglePortHidden(vm.Entry); });
        ResetPanelLayoutCommand = new RelayCommand(_ => Controller.ResetPanelLayout());
        ToggleEditorCommand = new RelayCommand(_ => IsEditorOpen = !IsEditorOpen);
        EnableAllCommand = new RelayCommand(_ => _ = Controller.EnableAllDisabledAsync(),
            _ => Controller.DisabledCount > 0 && !Controller.IsBusy);

        PortViewModel.EditorRequested += vm =>
        {
            SelectedPort = vm;
            IsEditorOpen = true;
        };
        // After a toggle, keep an already-open selection pinned to the same device across the
        // refresh that follows — but only when the toggled device is the one actually shown;
        // otherwise toggling any other tile while an editor is open would hijack it.
        Controller.PortFocused += entry =>
        {
            if (SelectedPort?.Entry.Device?.Identity == entry.Device?.Identity)
                SelectedPort = FindViewModel(entry);
        };
        Controller.PropertyChanged += (_, e) =>
        {
            // Tiles are rebuilt on every refresh: carry the selection ring over to the fresh
            // tile for the same port. The editor keeps its current view-model so typing in a
            // field isn't interrupted by the refresh that a rename triggers.
            if (e.PropertyName == nameof(AppController.Hubs) && SelectedPort is { } sel)
            {
                var fresh = Controller.Hubs.SelectMany(h => h.Ports).Concat(Controller.FrontPanelPorts)
                    .FirstOrDefault(vm => vm.PortKey == sel.PortKey);
                if (fresh is not null)
                    fresh.IsSelected = true;
            }

            if (e.PropertyName is nameof(AppController.IsBusy) or nameof(AppController.StatusText))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(StatusText));
            }
        };
        Controller.Hubs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoHubs));
    }

    /// <summary>The tile view-model wrapping a snapshot entry (null when the port is filtered out).</summary>
    private PortViewModel? FindViewModel(PortEntry entry) =>
        Controller.Hubs.SelectMany(h => h.Ports).Concat(Controller.FrontPanelPorts)
            .FirstOrDefault(vm => vm.Entry == entry);

    private async Task ApplySelectedProfileAsync()
    {
        if (SelectedProfile is { } p)
            await Controller.ApplyProfileAsync(p);
    }

    private void CaptureCurrent()
    {
        var name = ProfileNamePrompt?.Invoke("Save current state as profile");
        if (string.IsNullOrWhiteSpace(name))
            return;
        var profile = Controller.CaptureProfile(name.Trim());

        var note = ProfileNotePrompt?.Invoke("Describe this profile (optional)");
        if (!string.IsNullOrWhiteSpace(note))
        {
            profile.Note = note.Trim();
            Controller.Store.Save();
        }

        SelectedProfile = Controller.ProfileList
            .FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
