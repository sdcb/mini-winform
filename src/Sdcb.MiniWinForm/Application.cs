using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.ApplicationInstallationAndServicing;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;
public static class Application
{
    private static int _uiThreadId;
    private static Form? _mainForm;

    internal static HINSTANCE Instance { get; private set; }

    public static HighDpiMode HighDpiMode => NativeApplication.GetCurrentHighDpiMode();

    internal static int CurrentDpi => NativeApplication.GetCurrentDpi();

    public static void EnableVisualStyles()
    {
        NativeApplication.EnableVisualStyles();
    }

    public static bool SetHighDpiMode(HighDpiMode highDpiMode)
    {
        ValidateHighDpiMode(highDpiMode);

        if (NativeApplication.IsHighDpiModeLocked)
        {
            return false;
        }

        return NativeApplication.SetHighDpiMode(highDpiMode);
    }

    public static void Run(Form mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        InitializeThread();
        _mainForm = mainWindow;

        mainWindow.CreateHandle();
        bool synchronizationContextInstalled = InstallSynchronizationContext(mainWindow, out SynchronizationContext? previousSynchronizationContext);

        try
        {
            mainWindow.Show();
            RunMainMessageLoop();
        }
        finally
        {
            RestoreSynchronizationContext(synchronizationContextInstalled, previousSynchronizationContext);
            _mainForm = null;
        }
    }

    internal static DialogResult RunDialog(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        InitializeThread();
        form.ShowDialogWindow();
        bool synchronizationContextInstalled = InstallSynchronizationContext(form, out SynchronizationContext? previousSynchronizationContext);

        try
        {
            RunDialogMessageLoop(form);
            return form.DialogResult;
        }
        finally
        {
            RestoreSynchronizationContext(synchronizationContextInstalled, previousSynchronizationContext);
        }
    }

    internal static void OnFormDestroyed(Form form)
    {
        if (ReferenceEquals(form, _mainForm))
        {
            PInvoke.PostQuitMessage(0);
        }
    }

    private static void InitializeThread()
    {
        _uiThreadId = Environment.CurrentManagedThreadId;
        NativeApplication.LockHighDpiMode();
        NativeApplication.InitializeCommonControls();
        if (Instance == default)
        {
            Instance = NativeApplication.GetModuleHandle();
        }
    }

    private static void RunMainMessageLoop()
    {
        while (true)
        {
            if (!GetNextMessage(out MSG message))
            {
                return;
            }

            DispatchMessage(message);
        }
    }

    private static void RunDialogMessageLoop(Form form)
    {
        bool continueLoop = !form.CheckCloseDialog();
        while (continueLoop)
        {
            if (!GetNextMessage(out MSG message))
            {
                return;
            }

            DispatchMessage(message);
            continueLoop = !form.CheckCloseDialog();
        }
    }

    private static bool GetNextMessage(out MSG message)
    {
        BOOL result = PInvoke.GetMessage(out message, default, 0, 0);
        if (result == -1)
        {
            throw new InvalidOperationException($"GetMessage failed. LastError={Marshal.GetLastPInvokeError()}.");
        }

        return result;
    }

    private static void DispatchMessage(MSG message)
    {
        PInvoke.TranslateMessage(message);
        _ = PInvoke.DispatchMessage(message);
    }

    internal static void VerifyUiThread()
    {
        if (_uiThreadId != 0 && Environment.CurrentManagedThreadId != _uiThreadId)
        {
            throw new InvalidOperationException("Controls can only be accessed from the UI thread.");
        }
    }

    internal static bool IsUiThread => _uiThreadId != 0 && Environment.CurrentManagedThreadId == _uiThreadId;

    private static bool InstallSynchronizationContext(Control marshalingControl, out SynchronizationContext? previousContext)
    {
        SynchronizationContext? currentContext = SynchronizationContext.Current;
        if (currentContext is not null && currentContext.GetType() != typeof(SynchronizationContext))
        {
            previousContext = null;
            return false;
        }

        previousContext = currentContext;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext(marshalingControl));
        return true;
    }

    private static void RestoreSynchronizationContext(bool installed, SynchronizationContext? previousContext)
    {
        if (installed)
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void ValidateHighDpiMode(HighDpiMode highDpiMode)
    {
        if (!Enum.IsDefined(highDpiMode))
        {
            throw new ArgumentOutOfRangeException(nameof(highDpiMode));
        }
    }
}

internal static unsafe class NativeApplication
{
    private const string XpThemesManifestResourceName = "Sdcb.MiniWinForm.XPThemes.manifest";
    private const int NativeResourceManifestId = 2;
    private const uint ActctxFlagResourceNameValid = 0x00000008;
    private const uint ActctxFlagHmoduleValid = 0x00000080;
    private const nint DpiAwarenessContextUnawareValue = -1;
    private const nint DpiAwarenessContextSystemAwareValue = -2;
    private const nint DpiAwarenessContextPerMonitorAwareValue = -3;
    private const nint DpiAwarenessContextPerMonitorAwareV2Value = -4;
    private const nint DpiAwarenessContextUnawareGdiScaledValue = -5;

    private static bool _highDpiModeLocked;
    private static bool _visualStylesInitialized;
    private static Windows.Win32.ReleaseActCtxSafeHandle? _visualStylesActivationContext;
    private static nuint _visualStylesActivationCookie;

    internal static bool IsHighDpiModeLocked => _highDpiModeLocked;

    internal static HINSTANCE GetModuleHandle()
    {
        return PInvoke.GetModuleHandle((PCWSTR)null);
    }

    private static DPI_AWARENESS_CONTEXT DpiAwarenessContextUnaware => CreateDpiAwarenessContext(DpiAwarenessContextUnawareValue);

    private static DPI_AWARENESS_CONTEXT DpiAwarenessContextSystemAware => CreateDpiAwarenessContext(DpiAwarenessContextSystemAwareValue);

    private static DPI_AWARENESS_CONTEXT DpiAwarenessContextPerMonitorAware => CreateDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareValue);

    private static DPI_AWARENESS_CONTEXT DpiAwarenessContextPerMonitorAwareV2 => CreateDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2Value);

    private static DPI_AWARENESS_CONTEXT DpiAwarenessContextUnawareGdiScaled => CreateDpiAwarenessContext(DpiAwarenessContextUnawareGdiScaledValue);

    internal static void LockHighDpiMode()
    {
        _highDpiModeLocked = true;
    }

    internal static HighDpiMode GetCurrentHighDpiMode()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            DPI_AWARENESS_CONTEXT awarenessContext = PInvoke.GetThreadDpiAwarenessContext();

            if (PInvoke.AreDpiAwarenessContextsEqual(awarenessContext, DpiAwarenessContextUnawareGdiScaled))
            {
                return HighDpiMode.DpiUnawareGdiScaled;
            }

            if (PInvoke.AreDpiAwarenessContextsEqual(awarenessContext, DpiAwarenessContextPerMonitorAwareV2))
            {
                return HighDpiMode.PerMonitorV2;
            }

            if (PInvoke.AreDpiAwarenessContextsEqual(awarenessContext, DpiAwarenessContextPerMonitorAware))
            {
                return HighDpiMode.PerMonitor;
            }

            if (PInvoke.AreDpiAwarenessContextsEqual(awarenessContext, DpiAwarenessContextSystemAware))
            {
                return HighDpiMode.SystemAware;
            }

            return HighDpiMode.DpiUnaware;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(6, 3))
        {
            using Process currentProcess = Process.GetCurrentProcess();
            _ = PInvoke.GetProcessDpiAwareness(currentProcess.SafeHandle, out PROCESS_DPI_AWARENESS awareness);
            return awareness switch
            {
                PROCESS_DPI_AWARENESS.PROCESS_SYSTEM_DPI_AWARE => HighDpiMode.SystemAware,
                PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE => HighDpiMode.PerMonitor,
                _ => HighDpiMode.DpiUnaware,
            };
        }

        return PInvoke.IsProcessDPIAware() ? HighDpiMode.SystemAware : HighDpiMode.DpiUnaware;
    }

    internal static bool SetHighDpiMode(HighDpiMode highDpiMode)
    {
        bool success;

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063))
        {
            DPI_AWARENESS_CONTEXT awarenessContext = highDpiMode switch
            {
                HighDpiMode.SystemAware => DpiAwarenessContextSystemAware,
                HighDpiMode.PerMonitor => DpiAwarenessContextPerMonitorAware,
                HighDpiMode.PerMonitorV2 => PInvoke.IsValidDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2)
                    ? DpiAwarenessContextPerMonitorAwareV2
                    : DpiAwarenessContextSystemAware,
                HighDpiMode.DpiUnawareGdiScaled => PInvoke.IsValidDpiAwarenessContext(DpiAwarenessContextUnawareGdiScaled)
                    ? DpiAwarenessContextUnawareGdiScaled
                    : DpiAwarenessContextUnaware,
                _ => DpiAwarenessContextUnaware,
            };

            success = PInvoke.SetProcessDpiAwarenessContext(awarenessContext);
        }
        else if (OperatingSystem.IsWindowsVersionAtLeast(6, 3))
        {
            PROCESS_DPI_AWARENESS awareness = highDpiMode switch
            {
                HighDpiMode.SystemAware => PROCESS_DPI_AWARENESS.PROCESS_SYSTEM_DPI_AWARE,
                HighDpiMode.PerMonitor or HighDpiMode.PerMonitorV2 => PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE,
                _ => PROCESS_DPI_AWARENESS.PROCESS_DPI_UNAWARE,
            };

            success = PInvoke.SetProcessDpiAwareness(awareness).Succeeded;
        }
        else
        {
            success = highDpiMode switch
            {
                HighDpiMode.DpiUnaware or HighDpiMode.DpiUnawareGdiScaled => true,
                HighDpiMode.SystemAware or HighDpiMode.PerMonitor or HighDpiMode.PerMonitorV2 => PInvoke.SetProcessDPIAware(),
                _ => throw new ArgumentOutOfRangeException(nameof(highDpiMode)),
            };
        }

        if (success)
        {
            _highDpiModeLocked = true;
        }

        return success;
    }

    internal static int GetCurrentDpi(HWND handle = default)
    {
        HighDpiMode highDpiMode = GetCurrentHighDpiMode();
        if (highDpiMode is HighDpiMode.DpiUnaware or HighDpiMode.DpiUnawareGdiScaled)
        {
            return 96;
        }

        if (handle != default && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return (int)PInvoke.GetDpiForWindow(handle);
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return (int)PInvoke.GetDpiForSystem();
        }

        return 96;
    }

    private static DPI_AWARENESS_CONTEXT CreateDpiAwarenessContext(nint value)
    {
        return *(DPI_AWARENESS_CONTEXT*)&value;
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3002", Justification = "Single-file case is handled via the embedded manifest fallback.")]
    internal static void EnableVisualStyles()
    {
        if (_visualStylesInitialized)
        {
            return;
        }

        var module = typeof(NativeApplication).Module;
        Windows.Win32.FreeLibrarySafeHandle moduleHandle = PInvoke.GetModuleHandle(module.Name);
        if (!moduleHandle.IsInvalid)
        {
            // Normal library loads can activate the DLL's native manifest resource directly.
            ACTCTXW actCtx = new()
            {
                cbSize = (uint)Marshal.SizeOf<ACTCTXW>(),
                lpResourceName = (char*)NativeResourceManifestId,
                dwFlags = ActctxFlagHmoduleValid | ActctxFlagResourceNameValid,
                hModule = (HINSTANCE)moduleHandle.DangerousGetHandle(),
            };

            _visualStylesActivationContext = PInvoke.CreateActCtx(actCtx);

            if (_visualStylesActivationContext is not null
                && PInvoke.ActivateActCtx((HANDLE)_visualStylesActivationContext.DangerousGetHandle(), out _visualStylesActivationCookie))
            {
                _visualStylesInitialized = true;
                return;
            }
        }

        // Native AOT can fold the library into the app EXE, which may not carry this DLL manifest resource.
        using Stream? manifestStream = module.Assembly.GetManifestResourceStream(XpThemesManifestResourceName);
        if (manifestStream is null)
        {
            return;
        }

        string manifestPath = Path.Combine(Path.GetTempPath(), $"Sdcb.MiniWinForm.{Guid.NewGuid():N}.manifest");
        try
        {
            using (FileStream fileStream = new(manifestPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                manifestStream.CopyTo(fileStream);
            }

            ACTCTXW actCtx = new()
            {
                cbSize = (uint)Marshal.SizeOf<ACTCTXW>(),
            };

            fixed (char* manifestPathPointer = manifestPath)
            {
                actCtx.lpSource = manifestPathPointer;
                _visualStylesActivationContext = PInvoke.CreateActCtx(actCtx);
                if (PInvoke.ActivateActCtx((HANDLE)_visualStylesActivationContext.DangerousGetHandle(), out _visualStylesActivationCookie))
                {
                    _visualStylesInitialized = true;
                }
            }
        }
        finally
        {
            try
            {
                File.Delete(manifestPath);
            }
            catch
            {
            }
        }
    }

    internal static void InitializeCommonControls()
    {
        INITCOMMONCONTROLSEX init = new()
        {
            dwSize = (uint)Marshal.SizeOf<INITCOMMONCONTROLSEX>(),
            dwICC = INITCOMMONCONTROLSEX_ICC.ICC_WIN95_CLASSES,
        };

        if (!PInvoke.InitCommonControlsEx(in init))
        {
            throw new InvalidOperationException($"InitCommonControlsEx failed. LastError={Marshal.GetLastPInvokeError()}.");
        }
    }
}