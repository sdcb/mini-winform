using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class Image : IDisposable
{
    private readonly bool _ownsHandle;
    private IntPtr _handle;

    private Image(IntPtr handle, int width, int height, bool ownsHandle)
    {
        _handle = handle;
        Width = width;
        Height = height;
        _ownsHandle = ownsHandle;
    }

    ~Image()
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

    public int Width { get; }

    public int Height { get; }

    public static Image FromFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string fullPath = Path.GetFullPath(fileName);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Image file was not found.", fullPath);
        }

        NativeImage.GetBitmapFileSize(fullPath, out int width, out int height);
        IntPtr handle = NativeImage.LoadBitmapFromFile(fullPath);
        return new Image(handle, width, height, ownsHandle: true);
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
            _ = NativeImage.DeleteObject(handle);
        }
    }
}

internal static unsafe class NativeImage
{
    internal static IntPtr LoadBitmapFromFile(string path)
    {
        SafeFileHandle handle = PInvoke.LoadImage(
            hInst: null,
            name: path,
            GDI_IMAGE_TYPE.IMAGE_BITMAP,
            cx: 0,
            cy: 0,
            IMAGE_FLAGS.LR_LOADFROMFILE | IMAGE_FLAGS.LR_CREATEDIBSECTION);

        if (handle.IsInvalid)
        {
            throw new InvalidOperationException($"LoadImage failed for '{path}'. LastError={Marshal.GetLastPInvokeError()}.");
        }

        IntPtr bitmapHandle = handle.DangerousGetHandle();
        handle.SetHandleAsInvalid();
        return bitmapHandle;
    }

    internal static void GetBitmapFileSize(string path, out int width, out int height)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);

        if (reader.ReadByte() != 'B' || reader.ReadByte() != 'M')
        {
            throw new InvalidOperationException("Only Windows BMP files are supported by MiniWinForm.Image.");
        }

        stream.Position = 18;
        width = reader.ReadInt32();
        height = Math.Abs(reader.ReadInt32());
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The BMP file has invalid dimensions.");
        }
    }

    internal static bool DeleteObject(IntPtr handle) => PInvoke.DeleteObject(new HGDIOBJ(handle));

    internal static void DrawBitmap(HDC destination, Image image, int x, int y, int width, int height)
    {
        HDC source = PInvoke.CreateCompatibleDC(destination);
        if (source == default)
        {
            throw new InvalidOperationException($"CreateCompatibleDC failed. LastError={Marshal.GetLastPInvokeError()}.");
        }

        HGDIOBJ previous = PInvoke.SelectObject(source, new HGDIOBJ(image.Handle));
        try
        {
            _ = PInvoke.SetStretchBltMode(destination, STRETCH_BLT_MODE.STRETCH_HALFTONE);
            if (!PInvoke.StretchBlt(
                destination,
                x,
                y,
                width,
                height,
                source,
                0,
                0,
                image.Width,
                image.Height,
                ROP_CODE.SRCCOPY))
            {
                throw new InvalidOperationException($"StretchBlt failed. LastError={Marshal.GetLastPInvokeError()}.");
            }
        }
        finally
        {
            if (previous != default)
            {
                _ = PInvoke.SelectObject(source, previous);
            }

            _ = PInvoke.DeleteDC(source);
        }
    }
}