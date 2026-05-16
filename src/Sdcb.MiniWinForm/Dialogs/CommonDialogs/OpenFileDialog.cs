using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Controls.Dialogs;
using static Windows.Win32.UI.Controls.Dialogs.OPEN_FILENAME_FLAGS;

namespace Sdcb.MiniWinForm;

public sealed unsafe class OpenFileDialog : FileDialog
{
    public bool Multiselect { get; set; }

    public override void Reset()
    {
        base.Reset();
        CheckFileExists = true;
        Multiselect = false;
    }

    public Stream OpenFile() => new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read);

    public string SafeFileName => Path.GetFileName(FileName) ?? string.Empty;

    public string[] SafeFileNames => FileNames.Select(static fileName => Path.GetFileName(fileName) ?? string.Empty).ToArray();

    private protected override bool AllowMultiSelect => Multiselect;

    private protected override bool RunFileDialog(ref OPENFILENAMEW openFileName)
    {
        bool result = PInvoke.GetOpenFileName(ref openFileName);
        if (!result)
        {
            ThrowIfCommonDialogError();
        }

        return result;
    }

    private protected override OPEN_FILENAME_FLAGS BuildOptions() => base.BuildOptions() | OFN_HIDEREADONLY;

    private static void ThrowIfCommonDialogError()
    {
        COMMON_DLG_ERRORS error = PInvoke.CommDlgExtendedError();
        if (error != 0)
        {
            throw new InvalidOperationException($"Open file dialog failed. Error={error} ({Marshal.GetLastPInvokeError()}).");
        }
    }
}