using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class RadioButton : Control
{
    private bool _checked;

    public RadioButton()
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
            Checked = true;
        }
    }

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        NativeDraw.DrawCheckBox(drawItem, Text, _checked, isRadioButton: true, this);
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

        if (_checked)
        {
            UncheckSiblingRadioButtons();
        }

        if (raiseEvent)
        {
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UncheckSiblingRadioButtons()
    {
        if (Parent is null)
        {
            return;
        }

        foreach (Control sibling in Parent.Controls)
        {
            if (sibling is RadioButton radioButton && radioButton != this && radioButton.Checked)
            {
                radioButton.SetChecked(false, updateNative: true, raiseEvent: true);
            }
        }
    }
}