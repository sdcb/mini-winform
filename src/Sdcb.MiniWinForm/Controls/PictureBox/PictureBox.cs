using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class PictureBox : Control
{
    private Image? _image;
    private PictureBoxSizeMode _sizeMode;
    private int _savedWidth = 100;
    private int _savedHeight = 50;

    public PictureBox()
        : base(tabStop: false, width: 100, height: 50)
    {
    }

    public Image? Image
    {
        get => _image;
        set
        {
            Application.VerifyUiThread();
            if (_image == value)
            {
                return;
            }

            _image = value;
            AdjustSize();
            InvalidateImage();
        }
    }

    public PictureBoxSizeMode SizeMode
    {
        get => _sizeMode;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_sizeMode == value)
            {
                return;
            }

            bool leavingAutoSize = _sizeMode == PictureBoxSizeMode.AutoSize && value != PictureBoxSizeMode.AutoSize;
            if (value == PictureBoxSizeMode.AutoSize)
            {
                _savedWidth = Width;
                _savedHeight = Height;
            }

            _sizeMode = value;
            AdjustSize(restoreSavedSize: leavingAutoSize);
            InvalidateImage();
            SizeModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SizeModeChanged;

    internal override string NativeClassName => "STATIC";

    internal override WINDOW_STYLE NativeStyle => base.NativeStyle | (WINDOW_STYLE)NativeConstants.SS_OWNERDRAW;

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        NativeDraw.DrawPictureBox(drawItem, this);
        return true;
    }

    internal Image? GetImage() => _image;

    internal void GetImageRectangle(out int left, out int top, out int width, out int height)
    {
        left = 0;
        top = 0;
        width = Width;
        height = Height;

        if (_image is null)
        {
            return;
        }

        switch (SizeMode)
        {
            case PictureBoxSizeMode.Normal:
            case PictureBoxSizeMode.AutoSize:
                width = _image.Width;
                height = _image.Height;
                break;
            case PictureBoxSizeMode.StretchImage:
                break;
            case PictureBoxSizeMode.CenterImage:
                width = _image.Width;
                height = _image.Height;
                left = (Width - width) / 2;
                top = (Height - height) / 2;
                break;
            case PictureBoxSizeMode.Zoom:
                if (_image.Width <= 0 || _image.Height <= 0 || Width <= 0 || Height <= 0)
                {
                    width = 0;
                    height = 0;
                    return;
                }

                double ratio = Math.Min(Width / (double)_image.Width, Height / (double)_image.Height);
                width = Math.Max(1, (int)Math.Round(_image.Width * ratio));
                height = Math.Max(1, (int)Math.Round(_image.Height * ratio));
                left = (Width - width) / 2;
                top = (Height - height) / 2;
                break;
        }
    }

    private void AdjustSize(bool restoreSavedSize = false)
    {
        if (SizeMode == PictureBoxSizeMode.AutoSize && Image is not null)
        {
            Width = Image.Width;
            Height = Image.Height;
        }
        else if (restoreSavedSize)
        {
            Width = _savedWidth;
            Height = _savedHeight;
        }
    }

    private void InvalidateImage()
    {
        if (IsHandleCreated)
        {
            NativeControl.Invalidate(NativeHandle);
        }
    }
}