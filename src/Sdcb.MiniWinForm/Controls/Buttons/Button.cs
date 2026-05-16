using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class Button : Control
{
    private DialogResult _dialogResult;

    public Button()
        : base(tabStop: true, width: 75, height: 23)
    {
    }

    public DialogResult DialogResult
    {
        get => _dialogResult;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _dialogResult = value;
        }
    }

    public event EventHandler? Click;

    internal override string NativeClassName => "BUTTON";

    internal override WINDOW_STYLE NativeStyle => base.NativeStyle | (WINDOW_STYLE)NativeConstants.BS_OWNERDRAW;

    internal override void OnCommand(int notificationCode)
    {
        if (notificationCode == NativeConstants.BN_CLICKED)
        {
            Click?.Invoke(this, EventArgs.Empty);
            if (DialogResult != DialogResult.None && FindForm() is { Modal: true } form)
            {
                form.DialogResult = DialogResult;
                form.Close();
            }
        }
    }

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        NativeDraw.DrawButton(drawItem, Text, NativeHandle, this);
        return true;
    }
}