using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Controls.Dialogs;
using static Windows.Win32.UI.Controls.Dialogs.OPEN_FILENAME_FLAGS;

namespace Sdcb.MiniWinForm;

public sealed unsafe class SaveFileDialog : FileDialog
{
    public bool OverwritePrompt { get; set; }

    public override void Reset()
    {
        base.Reset();
        OverwritePrompt = true;
    }

    public Stream OpenFile() => new FileStream(FileName, FileMode.Create, FileAccess.ReadWrite);

    private protected override bool RunFileDialog(ref OPENFILENAMEW openFileName)
    {
        bool result = PInvoke.GetSaveFileName(ref openFileName);
        if (!result)
        {
            ThrowIfCommonDialogError();
        }

        return result;
    }

    private protected override OPEN_FILENAME_FLAGS BuildOptions()
    {
        OPEN_FILENAME_FLAGS flags = base.BuildOptions();
        if (OverwritePrompt)
        {
            flags |= OFN_OVERWRITEPROMPT;
        }

        return flags;
    }

    private static void ThrowIfCommonDialogError()
    {
        COMMON_DLG_ERRORS error = PInvoke.CommDlgExtendedError();
        if (error != 0)
        {
            throw new InvalidOperationException($"Save file dialog failed. Error={error} ({Marshal.GetLastPInvokeError()}).");
        }
    }
}