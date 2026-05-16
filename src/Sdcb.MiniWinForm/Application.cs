using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.ApplicationInstallationAndServicing;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public static class Application
{
    private static int _uiThreadId;
    private static Form? _mainForm;

    internal static HINSTANCE Instance { get; private set; }

    public static void EnableVisualStyles()
    {
        NativeApplication.EnableVisualStyles();
    }

    public static void Run(Form mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        InitializeThread();
        _mainForm = mainWindow;

        mainWindow.CreateHandle();
        mainWindow.Show();

        RunMainMessageLoop();
        _mainForm = null;
    }

    internal static DialogResult RunDialog(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        InitializeThread();
        form.ShowDialogWindow();
        RunDialogMessageLoop(form);
        return form.DialogResult;
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
}

internal static unsafe class NativeApplication
{
    private const string XpThemesManifestResourceName = "Sdcb.MiniWinForm.XPThemes.manifest";
    private const int NativeResourceManifestId = 2;
    private const uint ActctxFlagResourceNameValid = 0x00000008;
    private const uint ActctxFlagHmoduleValid = 0x00000080;

    private static bool _visualStylesInitialized;
    private static Windows.Win32.ReleaseActCtxSafeHandle? _visualStylesActivationContext;
    private static nuint _visualStylesActivationCookie;

    internal static HINSTANCE GetModuleHandle()
    {
        return PInvoke.GetModuleHandle((PCWSTR)null);
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