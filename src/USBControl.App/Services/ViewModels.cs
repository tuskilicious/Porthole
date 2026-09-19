using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using USBControl.Core;

namespace USBControl.App.Services;

/// <summary>Bridges a HubGroup snapshot node into the observable UI world.</summary>
public sealed class HubGroupViewModel
{
    public HubGroup Entry { get; }
    public AppController Controller { get; }
    public ObservableCollection<PortViewModel> Ports { get; } = new();

    public string DisplayName => Entry.DisplayName;

    public HubGroupViewModel(AppController controller, HubGroup entry)
    {
        Controller = controller;
        Entry = entry;
    }
}

/// <summary>One physical port tile: state, device, photo, editor fields, and actions.</summary>
public sealed class PortViewModel : INotifyPropertyChanged
{
    /// <summary>Raised when a tile asks to be opened in the editor panel (double-click).</summary>
    public static event Action<PortViewModel>? EditorRequested;

    public PortEntry Entry { get; }
    public AppController Controller { get; }

    private double _layoutX;
    private double _layoutY;

    public PortViewModel(AppController controller, PortEntry entry)
    {
        Controller = controller;
        Entry = entry;
        (_layoutX, _layoutY) = controller.GetPanelPos(entry.PortKey);

        ToggleCommand = new RelayCommand(
            _ => _ = Controller.ToggleAsync(Entry, Entry.State == PortState.Disabled),
            _ => Entry.Device is not null);
        OpenEditorCommand = new RelayCommand(_ => EditorRequested?.Invoke(this));
        PickPhotoCommand = new RelayCommand(_ => PickPhoto());
        ClearPhotoCommand = new RelayCommand(_ => Controller.ClearDevicePhoto(Entry),
            _ => HasPhoto);
    }

    public ICommand ToggleCommand { get; }
    public ICommand OpenEditorCommand { get; }
    public ICommand PickPhotoCommand { get; }
    public ICommand ClearPhotoCommand { get; }

    public UsbDeviceInfo? Device => Entry.Device;

    public string PortKey => Entry.PortKey;

    public string PortTitle
    {
        get
        {
            var meta = Controller.Store.GetOrCreatePort(Entry.PortKey);
            return string.IsNullOrWhiteSpace(meta.Label) ? $"Port {Entry.PortNumber}" : meta.Label;
        }
    }

    public string HubName
    {
        get
        {
            var hub = Controller.Hubs.FirstOrDefault(h => h.Entry.HubKey == Entry.HubKey);
            return hub?.DisplayName ?? Entry.HubKey;
        }
    }

    public string DeviceName => Entry.Device?.DisplayName switch
    {
        null => "Empty",
        "" => "Unknown device",
        var n => n,
    };

    public string Subtitle
    {
        get
        {
            if (Entry.Device is null)
                return $"hub {Entry.HubKey} · port {Entry.PortNumber}";
            var cls = string.IsNullOrEmpty(Entry.Device.ClassName) ? "" : $" · {Entry.Device.ClassName}";
            return $"{Entry.Device.Identity}{cls}";
        }
    }

    public string StateText => Entry.State switch
    {
        PortState.Connected => "Connected",
        PortState.Disabled => "Disabled",
        PortState.Problem => "Problem",
        PortState.Hub => "Hub",
        _ => "Empty",
    };

    public bool IsConnected => Entry.Device is not null && Entry.State != PortState.Hub;
    public bool IsEmpty => Entry.Device is null;
    public bool IsDisabled => Entry.State == PortState.Disabled;
    public bool IsProblem => Entry.State == PortState.Problem;
    public bool IsHub => Entry.Device?.IsHub ?? false;
    public bool GameRelevant => Entry.Device?.GameRelevant ?? false;

    public string InstanceId => Entry.Device?.InstanceId ?? "";

    // ---------------- panel layout (free-form view) ----------------

    /// <summary>Canvas X on the panel surface (binds to Canvas.Left).</summary>
    public double LayoutX { get => _layoutX; private set { _layoutX = value; OnPropertyChanged(nameof(LayoutX)); } }

    /// <summary>Canvas Y on the panel surface (binds to Canvas.Top).</summary>
    public double LayoutY { get => _layoutY; private set { _layoutY = value; OnPropertyChanged(nameof(LayoutY)); } }

    /// <summary>Called by the controller after a drop so the tile snaps to the grid.</summary>
    public void SetPosition(double x, double y)
    {
        LayoutX = x;
        LayoutY = y;
    }

    public string? PhotoPath =>
        Entry.Device is null
            ? null
            : Controller.PhotoPathFor(Controller.Store.GetOrCreateDevice(
                Entry.Device.Identity, Entry.Device.InstanceId, Entry.Device.DisplayName).PhotoFile);

    public bool HasPhoto => PhotoPath is not null;

    // ---------------- editor panel fields ----------------

    public string EditPortLabel
    {
        get => PortTitle;
        set
        {
            Controller.RenamePort(Entry, value);
            OnPropertyChanged(nameof(PortTitle));
        }
    }

    public string EditDeviceName
    {
        get => Entry.Device?.DisplayName ?? "";
        set
        {
            Controller.RenameDevice(Entry, value);
            OnPropertyChanged(nameof(DeviceName));
        }
    }

    public string EditNote
    {
        get
        {
            if (Entry.Device is null) return "";
            var meta = Controller.Store.GetOrCreateDevice(Entry.Device.Identity, Entry.Device.InstanceId, Entry.Device.DisplayName);
            return meta.Note;
        }
        set
        {
            if (Entry.Device is null) return;
            var meta = Controller.Store.GetOrCreateDevice(Entry.Device.Identity, Entry.Device.InstanceId, Entry.Device.DisplayName);
            if (meta.Note == value) return;
            meta.Note = value;
            Controller.Store.Save();
        }
    }

    private void PickPhoto()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a picture for this device",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog() != true)
            return;
        Controller.SetDevicePhoto(Entry, dlg.FileName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Loads a photo file into a UI-ready ImageSource, cached per path (decoding is expensive; tiles rebind constantly).</summary>
public sealed class PhotoConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;
        return Cache.GetOrAdd(path, static p =>
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // release the file handle immediately
                bmp.UriSource = new Uri(p, UriKind.Absolute);
                bmp.DecodePixelWidth = 320; // decode at display size, not full resolution
                bmp.EndInit();
                bmp.Freeze(); // thread-safe + no WPF bitmap overhead
                return (ImageSource)bmp;
            }
            catch
            {
                return null;
            }
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → Visibility, with optional inversion.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var b = value is bool v && v;
        if (Inverse) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
