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

    public ObservableCollection<Profile> ProfileList => Controller.ProfileList;

    public PortViewModel? SelectedPort
    {
        get => _selectedPort;
        set
        {
            _selectedPort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
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

        PortViewModel.EditorRequested += vm => SelectedPort = vm;
        Controller.PortFocused += entry => SelectedPort = FindViewModel(entry);
        Controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppController.IsBusy) or nameof(AppController.StatusText))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(StatusText));
            }
        };
        Controller.Hubs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoHubs));
    }

    private PortViewModel? FindViewModel(PortEntry entry) =>
        Controller.Hubs.SelectMany(h => h.Ports).FirstOrDefault(vm => vm.Entry == entry);

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
        Controller.CaptureProfile(name.Trim());
        SelectedProfile = Controller.ProfileList
            .FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
