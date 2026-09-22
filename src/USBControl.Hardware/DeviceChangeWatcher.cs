using System.Runtime.InteropServices;

namespace USBControl.Hardware;

/// <summary>
/// Background window that receives WM_DEVICECHANGE (DBT_DEVNODES_CHANGED) and raises debounced
/// change notifications. Kept out of WPF so it never touches the UI thread.
///
/// The window is created invisible but NOT message-only (parent is NULL, not HWND_MESSAGE):
/// Windows delivers DBT_DEVNODES_CHANGED via HWND_BROADCAST, which explicitly excludes
/// message-only windows ("A message-only window does not receive broadcast messages") but does
/// include invisible unowned top-level windows. A message-only window here would silently never
/// see a single device event, hot-plug detection included.
/// </summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    private Thread? _thread;
    private IntPtr _hwnd;
    private volatile bool _stop;
    private Native.WndProcDelegate? _wndProc; // rooted so the GC can't collect the callback
    private long _pending;
    private readonly CancellationTokenSource _cts = new();

    public event Action? Changed;

    public void Start()
    {
        if (_thread is not null)
            return;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "USBControl.DeviceWatcher",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ = Task.Run(DebounceLoopAsync);
    }

    private void Run()
    {
        _wndProc = WndProc;
        var className = "USBControlWatcher_" + Guid.NewGuid().ToString("N");

        var wc = new Native.WNDCLASSW
        {
            lpfnWndProc = _wndProc,
            hInstance = Native.GetModuleHandleW(null),
            lpszClassName = className,
        };
        Native.RegisterClassW(ref wc);

        _hwnd = Native.CreateWindowExW(0, className, "USBControlDeviceWatcher", 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        while (!_stop && Native.GetMessageW(out var msg, _hwnd, 0, 0) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
            Native.DestroyWindow(_hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Native.WM_DEVICECHANGE && wParam.ToInt64() == Native.DBT_DEVNODES_CHANGED)
            Interlocked.Increment(ref _pending);
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>Fires Changed at most ~2x/second after a 400 ms quiet period.</summary>
    private async Task DebounceLoopAsync()
    {
        long last = 0;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                var now = Interlocked.Read(ref _pending);
                if (now != last)
                {
                    last = now;
                    await Task.Delay(400, _cts.Token).ConfigureAwait(false); // let the burst settle
                    now = Interlocked.Read(ref _pending);
                    last = now;
                    Changed?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // keep the loop alive
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _cts.Cancel();
        if (_hwnd != IntPtr.Zero)
            Native.PostMessageW(_hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }
}
