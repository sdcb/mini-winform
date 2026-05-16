using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class StatusStrip : Control, IToolStripItemOwner
{
    public StatusStrip()
        : base(tabStop: false, width: 200, height: 22)
    {
        Dock = DockStyle.Bottom;
        Items = new ToolStripItemCollection(this);
    }

    public ToolStripItemCollection Items { get; }

    internal override string NativeClassName => "STATIC";

    internal override WINDOW_STYLE NativeStyle =>
        WINDOW_STYLE.WS_CHILD |
        WINDOW_STYLE.WS_CLIPSIBLINGS |
        (WINDOW_STYLE)NativeConstants.SS_OWNERDRAW;

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        NativeDraw.DrawStatusStrip(drawItem, this);
        return true;
    }

    ToolStripItem IToolStripItemOwner.CreateDefaultItem(string text) => new ToolStripStatusLabel(text);

    bool IToolStripItemOwner.CanOwnItem(ToolStripItem item) => item is ToolStripStatusLabel;

    void IToolStripItemOwner.NotifyItemsChanged()
    {
        if (IsHandleCreated)
        {
            NativeControl.Invalidate(NativeHandle);
        }
    }
}