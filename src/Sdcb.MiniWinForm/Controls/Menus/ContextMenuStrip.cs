using System.Drawing;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class ContextMenuStrip : IToolStripItemOwner
{
    private readonly Dictionary<int, ToolStripMenuItem> _menuItemsById = [];

    public ContextMenuStrip()
    {
        Items = new ToolStripItemCollection(this);
    }

    public ToolStripItemCollection Items { get; }

    public Control? SourceControl { get; private set; }

    public void Show(Control control, Point position)
    {
        ArgumentNullException.ThrowIfNull(control);
        Show(control, position.X, position.Y);
    }

    public void Show(Control control, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(control);
        Application.VerifyUiThread();

        if (!control.IsHandleCreated)
        {
            throw new InvalidOperationException("The source control needs a created handle before showing a context menu.");
        }

        NativeMenu.GetWindowRect(control.NativeHandle, out int left, out int top, out _, out _);
        ShowAtScreenLocation(control, left + x, top + y);
    }

    internal void ShowAtScreenLocation(Control sourceControl, int screenX, int screenY)
    {
        Application.VerifyUiThread();
        SourceControl = sourceControl;

        if (Items.Count == 0)
        {
            return;
        }

        _menuItemsById.Clear();
        HMENU menu = NativeMenu.BuildPopup(Items, _menuItemsById);
        try
        {
            int commandId = NativeMenu.TrackPopup(menu, sourceControl.NativeHandle, screenX, screenY);
            if (commandId != 0 && _menuItemsById.TryGetValue(commandId, out ToolStripMenuItem? item))
            {
                item.PerformClick();
            }
        }
        finally
        {
            NativeMenu.Destroy(menu);
            _menuItemsById.Clear();
        }
    }

    ToolStripItem IToolStripItemOwner.CreateDefaultItem(string text) => new ToolStripMenuItem(text);

    bool IToolStripItemOwner.CanOwnItem(ToolStripItem item) => item is ToolStripMenuItem;

    void IToolStripItemOwner.NotifyItemsChanged()
    {
    }
}