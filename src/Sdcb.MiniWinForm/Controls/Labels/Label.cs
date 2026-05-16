using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class Label : Control
{
    public Label()
        : base(tabStop: false, width: 100, height: 23)
    {
    }

    internal override string NativeClassName => "STATIC";

    internal override WINDOW_STYLE NativeStyle =>
        WINDOW_STYLE.WS_CHILD |
        WINDOW_STYLE.WS_CLIPSIBLINGS;
}