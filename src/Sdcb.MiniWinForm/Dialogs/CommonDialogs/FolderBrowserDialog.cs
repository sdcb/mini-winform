using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Sdcb.MiniWinForm;

public sealed unsafe class FolderBrowserDialog : CommonDialog
{
    private static readonly BFFCALLBACK BrowseCallbackDelegate = BrowseCallback;

    private const uint BifReturnOnlyFileSystemDirs = 0x0001;
    private const uint BifNewDialogStyle = 0x0040;
    private const uint BifNoNewFolderButton = 0x0200;
    private const uint BffmInitialized = 1;
    private const uint BffmSetSelection = NativeConstants.WM_USER + 103;

    private string _description = string.Empty;
    private string _selectedPath = string.Empty;

    public FolderBrowserDialog()
    {
        Reset();
    }

    public string Description
    {
        get => _description;
        set => _description = value ?? string.Empty;
    }

    public string SelectedPath
    {
        get => _selectedPath;
        set => _selectedPath = value ?? string.Empty;
    }

    public bool ShowNewFolderButton { get; set; }

    public override void Reset()
    {
        Description = string.Empty;
        SelectedPath = string.Empty;
        ShowNewFolderButton = true;
    }

    private protected override bool RunDialog(HWND owner)
    {
        char[] displayName = new char[260];
        uint flags = BifReturnOnlyFileSystemDirs | BifNewDialogStyle;
        if (!ShowNewFolderButton)
        {
            flags |= BifNoNewFolderButton;
        }

        GCHandle ownerHandle = GCHandle.Alloc(this);
        try
        {
            fixed (char* displayNamePointer = displayName)
            fixed (char* descriptionPointer = Description.Length == 0 ? null : Description)
            {
                BROWSEINFOW browseInfo = new()
                {
                    hwndOwner = owner,
                    pszDisplayName = displayNamePointer,
                    lpszTitle = descriptionPointer,
                    ulFlags = flags,
                    lpfn = BrowseCallbackDelegate,
                    lParam = new LPARAM(GCHandle.ToIntPtr(ownerHandle)),
                };

                ITEMIDLIST* itemIdList = PInvoke.SHBrowseForFolder(in browseInfo);
                if (itemIdList is null)
                {
                    SelectedPath = string.Empty;
                    return false;
                }

                try
                {
                    char[] pathBuffer = new char[260];
                    if (!PInvoke.SHGetPathFromIDList(in *itemIdList, pathBuffer))
                    {
                        SelectedPath = string.Empty;
                        return false;
                    }

                    SelectedPath = StringFromNullTerminatedBuffer(pathBuffer);
                    return SelectedPath.Length > 0;
                }
                finally
                {
                    PInvoke.CoTaskMemFree(itemIdList);
                }
            }
        }
        finally
        {
            ownerHandle.Free();
        }
    }

    private static int BrowseCallback(HWND window, uint message, LPARAM lParam, LPARAM data)
    {
        _ = lParam;
        if (message != BffmInitialized)
        {
            return 0;
        }

        FolderBrowserDialog dialog = (FolderBrowserDialog)GCHandle.FromIntPtr(data).Target!;
        if (dialog.Description.Length > 0)
        {
            _ = PInvoke.SetWindowText(window, dialog.Description);
        }

        if (dialog.SelectedPath.Length > 0)
        {
            unsafe
            {
                fixed (char* selectedPath = dialog.SelectedPath)
                {
                    _ = PInvoke.SendMessage(window, BffmSetSelection, new WPARAM(1), new LPARAM((nint)selectedPath));
                }
            }
        }

        return 0;
    }

    private static string StringFromNullTerminatedBuffer(char[] buffer)
    {
        int length = Array.IndexOf(buffer, '\0');
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }
}