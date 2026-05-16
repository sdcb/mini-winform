using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

[DefaultProperty(nameof(Interval))]
[DefaultEvent(nameof(Tick))]
public class Timer : Component
{
    private int _interval = 100;
    private bool _enabled;
    private TimerNativeWindow? _timerWindow;

    public Timer()
    {
    }

    public Timer(IContainer container)
        : this()
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Add(this);
    }

    [DefaultValue(null)]
    public object? Tag { get; set; }

    public event EventHandler? Tick;

    [DefaultValue(false)]
    public virtual bool Enabled
    {
        get => _timerWindow is null ? _enabled : _timerWindow.IsTimerRunning;
        set
        {
            Application.VerifyUiThread();
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (value)
            {
                _timerWindow ??= new TimerNativeWindow(this);
                _timerWindow.StartTimer(_interval);
            }
            else
            {
                _timerWindow?.StopTimer();
            }
        }
    }

    [DefaultValue(100)]
    public int Interval
    {
        get => _interval;
        set
        {
            Application.VerifyUiThread();
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Interval must be greater than 0.");
            }

            if (_interval == value)
            {
                return;
            }

            _interval = value;
            if (Enabled)
            {
                _timerWindow?.RestartTimer(value);
            }
        }
    }

    public void Start() => Enabled = true;

    public void Stop() => Enabled = false;

    public override string ToString() => $"{base.ToString()}, Interval: {Interval}";

    protected virtual void OnTick(EventArgs e) => Tick?.Invoke(this, e);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
        }

        _timerWindow = null;
        base.Dispose(disposing);
    }

    private sealed unsafe class TimerNativeWindow
    {
        private static readonly NativeWindowClass NativeClass = new("MiniWinForm.Timer");
        private static readonly WNDPROC SharedWindowProcedure = WindowProcedure;
        private static readonly Dictionary<nint, TimerNativeWindow> TimersByHandle = [];
        private static nuint NextTimerId = 1;

        private readonly Timer _owner;
        private HWND _handle;
        private nuint _timerId;

        internal TimerNativeWindow(Timer owner)
        {
            _owner = owner;
        }

        internal bool IsTimerRunning => _timerId != 0 && _handle != default;

        internal void StartTimer(int interval)
        {
            if (_timerId != 0)
            {
                return;
            }

            EnsureHandle();
            _timerId = PInvoke.SetTimer(_handle, NextTimerId++, (uint)interval, null);
            if (_timerId == 0)
            {
                throw new InvalidOperationException($"SetTimer failed. LastError={Marshal.GetLastPInvokeError()}.");
            }
        }

        internal void RestartTimer(int interval)
        {
            StopTimer(destroyHandle: false);
            StartTimer(interval);
        }

        internal void StopTimer() => StopTimer(destroyHandle: true);

        private void StopTimer(bool destroyHandle)
        {
            if (_timerId != 0 && _handle != default)
            {
                if (!PInvoke.KillTimer(_handle, _timerId))
                {
                    throw new InvalidOperationException($"KillTimer failed. LastError={Marshal.GetLastPInvokeError()}.");
                }

                _timerId = 0;
            }

            if (destroyHandle && _handle != default)
            {
                HWND handle = _handle;
                _handle = default;
                TimersByHandle.Remove((nint)handle.Value);
                NativeControl.Destroy(handle);
            }
        }

        private void EnsureHandle()
        {
            if (_handle != default)
            {
                return;
            }

            NativeClass.Register(SharedWindowProcedure);
            fixed (char* classNamePtr = NativeClass.Name)
            {
                _handle = PInvoke.CreateWindowEx(
                    0,
                    new PCWSTR(classNamePtr),
                    default,
                    0,
                    0,
                    0,
                    0,
                    0,
                    new HWND(NativeConstants.HWND_MESSAGE),
                    default,
                    Application.Instance != default ? Application.Instance : NativeApplication.GetModuleHandle(),
                    null);
            }

            if (_handle == default)
            {
                throw new InvalidOperationException($"CreateWindowEx failed for timer window. LastError={Marshal.GetLastPInvokeError()}.");
            }

            TimersByHandle[(nint)_handle.Value] = this;
        }

        private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
        {
            if (!TimersByHandle.TryGetValue((nint)window.Value, out TimerNativeWindow? timerWindow))
            {
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
            }

            switch (message)
            {
                case NativeConstants.WM_TIMER:
                    if (wParam.Value == timerWindow._timerId)
                    {
                        timerWindow._owner.OnTick(EventArgs.Empty);
                        return new LRESULT(0);
                    }

                    break;
                case NativeConstants.WM_CLOSE:
                    timerWindow.StopTimer();
                    return new LRESULT(0);
                case NativeConstants.WM_DESTROY:
                    timerWindow._timerId = 0;
                    timerWindow._handle = default;
                    TimersByHandle.Remove((nint)window.Value);
                    return new LRESULT(0);
            }

            return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
    }
}