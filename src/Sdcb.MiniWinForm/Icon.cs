using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class Icon : IDisposable
{
    private readonly bool _ownsHandle;
    private IntPtr _handle;

    private Icon(IntPtr handle, bool ownsHandle)
    {
        _handle = handle;
        _ownsHandle = ownsHandle;
    }

    ~Icon()
    {
        Dispose(disposing: false);
    }

    public IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
            return _handle;
        }
    }

    public static Icon FromFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string fullPath = Path.GetFullPath(fileName);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Icon file was not found.", fullPath);
        }

        IntPtr handle = NativeIcon.LoadFromFile(fullPath);
        return new Icon(handle, ownsHandle: true);
    }

    public static Icon FromHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            throw new ArgumentException("Icon handle cannot be zero.", nameof(handle));
        }

        return new Icon(handle, ownsHandle: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        _ = disposing;

        if (_handle == IntPtr.Zero)
        {
            return;
        }

        IntPtr handle = _handle;
        _handle = IntPtr.Zero;

        if (_ownsHandle)
        {
            _ = NativeIcon.DestroyIcon(handle);
        }
    }
}

internal static class NativeIcon
{
    internal static IntPtr LoadFromFile(string path)
    {
        SafeFileHandle handle = PInvoke.LoadImage(
            hInst: null,
            name: path,
            GDI_IMAGE_TYPE.IMAGE_ICON,
            cx: 0,
            cy: 0,
            IMAGE_FLAGS.LR_LOADFROMFILE | IMAGE_FLAGS.LR_DEFAULTSIZE);

        if (handle.IsInvalid)
        {
            throw new InvalidOperationException($"LoadImage failed for '{path}'. LastError={Marshal.GetLastPInvokeError()}.");
        }

        IntPtr iconHandle = handle.DangerousGetHandle();
        handle.SetHandleAsInvalid();
        return iconHandle;
    }

    internal static void SetWindowIcon(IntPtr windowHandle, Icon? icon, bool redrawFrame)
    {
        IntPtr iconHandle = icon is null ? IntPtr.Zero : icon.Handle;
        HWND window = new(windowHandle);

        _ = PInvoke.SendMessage(window, NativeConstants.WM_SETICON, new WPARAM(NativeConstants.ICON_SMALL), new LPARAM(iconHandle));
        _ = PInvoke.SendMessage(window, NativeConstants.WM_SETICON, new WPARAM(NativeConstants.ICON_BIG), new LPARAM(iconHandle));

        if (redrawFrame)
        {
            unsafe
            {
                _ = PInvoke.RedrawWindow(window, lprcUpdate: null, HRGN.Null, REDRAW_WINDOW_FLAGS.RDW_INVALIDATE | REDRAW_WINDOW_FLAGS.RDW_FRAME);
            }
        }
    }

    internal static bool DestroyIcon(IntPtr icon) => PInvoke.DestroyIcon(new HICON(icon));
}