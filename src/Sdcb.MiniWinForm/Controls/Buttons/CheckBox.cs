using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class CheckBox : Control
{
    private bool _checked;

    public CheckBox()
        : base(tabStop: true, width: 104, height: 24)
    {
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get
        {
            Application.VerifyUiThread();
            return _checked;
        }
        set
        {
            Application.VerifyUiThread();
            SetChecked(value, updateNative: true, raiseEvent: true);
        }
    }

    internal override string NativeClassName => "BUTTON";

    internal override WINDOW_STYLE NativeStyle =>
        base.NativeStyle |
        (WINDOW_STYLE)NativeConstants.BS_OWNERDRAW;

    internal override void CreateHandle()
    {
        base.CreateHandle();
        NativeControl.SetCheckState(NativeHandle, _checked);
    }

    internal override void OnCommand(int notificationCode)
    {
        if (notificationCode == NativeConstants.BN_CLICKED)
        {
            SetChecked(!Checked, updateNative: true, raiseEvent: true);
        }
    }

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        NativeDraw.DrawCheckBox(drawItem, Text, _checked, isRadioButton: false, this);
        return true;
    }

    private void SetChecked(bool value, bool updateNative, bool raiseEvent)
    {
        if (_checked == value)
        {
            return;
        }

        _checked = value;
        if (updateNative && IsHandleCreated)
        {
            NativeControl.SetCheckState(NativeHandle, _checked);
            NativeControl.Invalidate(NativeHandle);
        }

        if (raiseEvent)
        {
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}