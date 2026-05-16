using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class GroupBox : Control
{
    public GroupBox()
        : base(tabStop: false, width: 200, height: 100)
    {
    }

    internal override string NativeClassName => "BUTTON";

    internal override WINDOW_STYLE NativeStyle =>
        WINDOW_STYLE.WS_CHILD |
        WINDOW_STYLE.WS_CLIPSIBLINGS |
        WINDOW_STYLE.WS_CLIPCHILDREN |
        (WINDOW_STYLE)NativeConstants.BS_OWNERDRAW;

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CONTROLPARENT;

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        NativeDraw.DrawGroupBox(drawItem, Text, this);
        return true;
    }
}
