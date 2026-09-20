using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using USBControl.Core;

namespace USBControl.App.Services;

/// <summary>
/// Shell_NotifyIcon-based tray icon (no WinForms dependency). Left click / double click
/// opens the window, right click opens a dark context menu with one-click profile
/// switching, and the tooltip mirrors the live status text.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    // ---------------- Win32 ----------------

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const int NIIF_INFO = 0x00000001;

    private const uint WM_APP_TRAY = 0x8000 + 0x0301; // WM_APP range, app-unique
    private const int NIN_BALLOONUSERCLICK = 0x0400 + 5;

    private const int WM_LBUTTONUP = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] presbits, uint dwResSize,
        bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    // The app runs elevated; Explorer does not, so its "taskbar was (re)created" broadcast is
    // dropped by UIPI unless we explicitly allow it through.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    private const uint MSGFLT_ALLOW = 1;
    private static readonly IntPtr IDI_APPLICATION = new(32512);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    // ---------------- state ----------------

    private readonly AppController _controller;
    private readonly MainWindowViewModel _vm;
    private HwndSource? _source;
    private IntPtr _hIcon;
    private bool _ownsIcon;
    private uint _taskbarCreatedMsg;
    private bool _iconAdded;
    private bool _disposed;

    public TrayIconController(AppController controller, MainWindowViewModel vm)
    {
        _controller = controller;
        _vm = vm;

        _hIcon = CreateTrayIconHandle();
        _ownsIcon = _hIcon != IntPtr.Zero;
        if (_hIcon == IntPtr.Zero)
            _hIcon = LoadIconW(IntPtr.Zero, IDI_APPLICATION); // never add an image-less (invisible) entry
        CreateMessageWindow();
        AddTrayIcon();

        _controller.PropertyChanged += OnControllerPropertyChanged;
    }

    // ---------------- message window ----------------

    private void CreateMessageWindow()
    {
        var parameters = new HwndSourceParameters("USBControlTrayWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");
        ChangeWindowMessageFilterEx(_source.Handle, _taskbarCreatedMsg, MSGFLT_ALLOW, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
        {
            // Explorer restarted and wiped the tray: put the icon back.
            AddTrayIcon();
            return IntPtr.Zero;
        }

        if (msg != (int)WM_APP_TRAY)
            return IntPtr.Zero;

        handled = true;
        switch (lParam.ToInt32())
        {
            case WM_LBUTTONUP:
            case WM_LBUTTONDBLCLK:
            case NIN_BALLOONUSERCLICK:
                ShowFromTray();
                break;
            case WM_RBUTTONUP:
                ShowContextMenu();
                break;
        }
        return IntPtr.Zero;
    }

    // ---------------- actions ----------------

    private void ShowFromTray()
    {
        var win = Application.Current?.MainWindow;
        if (win is null)
            return;
        win.Show();
        if (win.WindowState == WindowState.Minimized)
            win.WindowState = WindowState.Normal;
        win.Activate();
    }

    private void ShowContextMenu()
    {
        var menu = BuildContextMenu();
        var pos = GetCursorPositionDip();
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = pos.X;
        menu.VerticalOffset = pos.Y;
        menu.IsOpen = true;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open USB Control", FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => ShowFromTray();
        menu.Items.Add(open);

        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem { Header = "Profiles", IsEnabled = false });

        var busy = _controller.IsBusy;
        var active = _vm.SelectedProfile?.Name;
        foreach (var profile in _vm.ProfileList)
        {
            var isActive = string.Equals(profile.Name, active, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem
            {
                Header = (isActive ? "● " : "") + profile.Name.Replace("_", "__"),
                IsEnabled = !busy,
            };
            var captured = profile;
            item.Click += (_, _) => _ = ApplyProfileFromTrayAsync(captured);
            menu.Items.Add(item);
        }
        if (_vm.ProfileList.Count == 0)
            menu.Items.Add(new MenuItem { Header = "(no profiles yet — save one in the app)", IsEnabled = false });

        menu.Items.Add(new Separator());

        var refresh = new MenuItem { Header = "Refresh now", IsEnabled = !busy };
        refresh.Click += (_, _) => _ = _controller.RefreshAsync();
        menu.Items.Add(refresh);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => App.RequestShutdown();
        menu.Items.Add(exit);

        return menu;
    }

    private async Task ApplyProfileFromTrayAsync(Profile profile)
    {
        if (_controller.IsBusy)
            return;
        _vm.SelectedProfile = profile;
        await _controller.ApplyProfileAsync(profile);
        ShowBalloon("USB Control", _controller.StatusText);
    }

    // ---------------- tooltip / balloon ----------------

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppController.StatusText) or nameof(AppController.IsBusy))
            UpdateTooltip("USB Control — " + _controller.StatusText);
    }

    private void UpdateTooltip(string text)
    {
        if (!_iconAdded)
            return;
        var nid = NewNid();
        nid.uFlags = NIF_TIP;
        nid.szTip = text.Length > 127 ? text[..127] : text;
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    public void ShowBalloon(string title, string text)
    {
        if (!_iconAdded)
            return;
        var nid = NewNid();
        nid.uFlags = NIF_INFO;
        nid.szInfoTitle = title.Length > 63 ? title[..63] : title;
        nid.szInfo = text.Length > 255 ? text[..255] : text;
        nid.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    // ---------------- notifyicon plumbing ----------------

    private NOTIFYICONDATAW NewNid()
    {
        return new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _source!.Handle,
            uID = 1,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _hIcon,
        };
    }

    private void AddTrayIcon()
    {
        var nid = NewNid();
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        nid.szTip = "USB Control";
        _iconAdded = Shell_NotifyIconW(NIM_ADD, ref nid);
    }

    // ---------------- icon rendering (code-drawn USB glyph) ----------------

    private static IntPtr CreateTrayIconHandle()
    {
        var accent = new SolidColorBrush((Color)Application.Current.Resources["AccentColor"]);
        accent.Freeze();
        var bg = new SolidColorBrush((Color)Application.Current.Resources["RaisedColor"]);
        bg.Freeze();

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var plate = new RectangleGeometry(new Rect(1.5, 1.5, 29, 29), 7, 7);
            plate.Freeze();
            dc.DrawGeometry(bg, null, plate);

            var pen = new Pen(accent, 3)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            pen.Freeze();

            dc.DrawLine(pen, new Point(16, 6), new Point(16, 11)); // prong tip
            var body = new RectangleGeometry(new Rect(13, 11, 6, 12), 2, 2);
            body.Freeze();
            dc.DrawGeometry(null, pen, body);                      // connector body
            var cable = new RectangleGeometry(new Rect(14.5, 23, 3, 5), 1.5, 1.5);
            cable.Freeze();
            dc.DrawGeometry(accent, null, cable);                  // cable stub
        }

        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);

        // CreateIconFromResourceEx takes raw icon resource bits; for PNG that is the PNG itself.
        // (Wrapping it in an .ico file header makes the call fail and return NULL, which left
        // the tray entry with no image, so nothing was visible.)
        var png = ms.ToArray();
        return CreateIconFromResourceEx(png, (uint)png.Length, fIcon: true,
            dwVer: 0x00030000, 32, 32, flags: 0);
    }

    private static Point GetCursorPositionDip()
    {
        if (!GetCursorPos(out var p))
            return default;
        var source = PresentationSource.FromVisual(Application.Current?.MainWindow);
        var m = source?.CompositionTarget?.TransformToDevice;
        return new Point(p.X / m?.M11 ?? p.X, p.Y / m?.M22 ?? p.Y);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _controller.PropertyChanged -= OnControllerPropertyChanged;

        if (_iconAdded)
        {
            var nid = NewNid();
            Shell_NotifyIconW(NIM_DELETE, ref nid);
            _iconAdded = false;
        }

        if (_hIcon != IntPtr.Zero && _ownsIcon)
            DestroyIcon(_hIcon);
        _hIcon = IntPtr.Zero;

        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }
}
