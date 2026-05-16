using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class Panel : Control
{
    public Panel()
        : base(tabStop: false, width: 200, height: 100)
    {
    }

    internal override string NativeClassName => "STATIC";

    internal override WINDOW_STYLE NativeStyle =>
        WINDOW_STYLE.WS_CHILD |
        WINDOW_STYLE.WS_CLIPSIBLINGS |
        WINDOW_STYLE.WS_CLIPCHILDREN;
}