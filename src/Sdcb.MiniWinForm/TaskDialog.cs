using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;

namespace Sdcb.MiniWinForm;

public sealed class TaskDialogPage
{
    public string? Caption { get; set; }

    public string? Heading { get; set; }

    public string? Text { get; set; }

    public TaskDialogButtonCollection Buttons { get; } = [];
}

public sealed class TaskDialogButtonCollection : List<TaskDialogButton>;

public sealed class TaskDialogButton
{
    private TaskDialogButton(int id, TASKDIALOG_COMMON_BUTTON_FLAGS flag, string text)
    {
        Id = id;
        Flag = flag;
        Text = text;
    }

    public string Text { get; }

    internal int Id { get; }

    internal TASKDIALOG_COMMON_BUTTON_FLAGS Flag { get; }

    public static TaskDialogButton OK => new(1, TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_OK_BUTTON, "OK");

    public static TaskDialogButton Yes => new(6, TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_YES_BUTTON, "Yes");

    public static TaskDialogButton No => new(7, TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_NO_BUTTON, "No");

    public static TaskDialogButton Cancel => new(2, TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_CANCEL_BUTTON, "Cancel");

    public static TaskDialogButton Retry => new(4, TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_RETRY_BUTTON, "Retry");

    public static TaskDialogButton Close => new(8, TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_CLOSE_BUTTON, "Close");

    public override string ToString() => Text;
}

public enum TaskDialogStartupLocation
{
    CenterScreen = 0,
    CenterOwner = 1,
}

public static unsafe class TaskDialog
{
    public static TaskDialogButton ShowDialog(
        TaskDialogPage page,
        TaskDialogStartupLocation startupLocation = TaskDialogStartupLocation.CenterOwner)
    {
        return ShowDialog(0, page, startupLocation);
    }

    public static TaskDialogButton ShowDialog(
        Form owner,
        TaskDialogPage page,
        TaskDialogStartupLocation startupLocation = TaskDialogStartupLocation.CenterOwner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return ShowDialog((nint)owner.NativeHandle.Value, page, startupLocation);
    }

    public static TaskDialogButton ShowDialog(
        nint hwndOwner,
        TaskDialogPage page,
        TaskDialogStartupLocation startupLocation = TaskDialogStartupLocation.CenterOwner)
    {
        ArgumentNullException.ThrowIfNull(page);
        ValidateStartupLocation(startupLocation);

        int hResult = ShowCore(new HWND(hwndOwner), page, out int selectedButton);
        if (hResult < 0)
        {
            throw new InvalidOperationException($"TaskDialog failed with HRESULT 0x{unchecked((uint)hResult):X8}.");
        }

        return FindButton(page, selectedButton);
    }

    private static int ShowCore(HWND owner, TaskDialogPage page, out int selectedButton)
    {
        TASKDIALOG_COMMON_BUTTON_FLAGS buttons = GetCommonButtonFlags(page);

        return PInvoke.TaskDialog(
            owner,
            null!,
            page.Caption ?? string.Empty,
            page.Heading ?? string.Empty,
            page.Text ?? string.Empty,
            buttons,
            null!,
            out selectedButton).Value;
    }

    private static TASKDIALOG_COMMON_BUTTON_FLAGS GetCommonButtonFlags(TaskDialogPage page)
    {
        if (page.Buttons.Count == 0)
        {
            return TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_OK_BUTTON;
        }

        TASKDIALOG_COMMON_BUTTON_FLAGS flags = 0;
        foreach (TaskDialogButton button in page.Buttons)
        {
            ArgumentNullException.ThrowIfNull(button);
            flags |= button.Flag;
        }

        return flags;
    }

    private static TaskDialogButton FindButton(TaskDialogPage page, int selectedButton)
    {
        foreach (TaskDialogButton button in page.Buttons)
        {
            if (button.Id == selectedButton)
            {
                return button;
            }
        }

        return selectedButton switch
        {
            1 => TaskDialogButton.OK,
            2 => TaskDialogButton.Cancel,
            4 => TaskDialogButton.Retry,
            6 => TaskDialogButton.Yes,
            7 => TaskDialogButton.No,
            8 => TaskDialogButton.Close,
            _ => TaskDialogButton.OK,
        };
    }

    private static void ValidateStartupLocation(TaskDialogStartupLocation startupLocation)
    {
        if (startupLocation is not TaskDialogStartupLocation.CenterScreen and not TaskDialogStartupLocation.CenterOwner)
        {
            throw new ArgumentOutOfRangeException(nameof(startupLocation));
        }
    }
}