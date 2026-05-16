using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public static class Application
{
    private static int _uiThreadId;
    private static Form? _mainForm;

    internal static HINSTANCE Instance { get; private set; }

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
    internal static HINSTANCE GetModuleHandle()
    {
        return PInvoke.GetModuleHandle((PCWSTR)null);
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