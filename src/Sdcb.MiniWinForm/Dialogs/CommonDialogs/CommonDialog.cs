using Windows.Win32.Foundation;

namespace Sdcb.MiniWinForm;

public abstract class CommonDialog
{
    private bool _inShowDialog;

    public object? Tag { get; set; }

    public DialogResult ShowDialog() => ShowDialog(owner: null);

    public DialogResult ShowDialog(IWin32Window? owner)
    {
        Application.VerifyUiThread();

        if (_inShowDialog)
        {
            return DialogResult.Cancel;
        }

        _inShowDialog = true;
        try
        {
            HWND ownerHandle = owner is null ? default : new HWND(owner.Handle);
            return RunDialog(ownerHandle) ? DialogResult.OK : DialogResult.Cancel;
        }
        finally
        {
            _inShowDialog = false;
        }
    }

    public abstract void Reset();

    private protected abstract bool RunDialog(HWND owner);
}