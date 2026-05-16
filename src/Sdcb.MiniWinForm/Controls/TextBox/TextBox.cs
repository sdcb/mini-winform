using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class TextBox : Control
{
    private bool _multiline;
    private bool _wordWrap = true;
    private ScrollBars _scrollBars = ScrollBars.None;

    public TextBox()
        : base(tabStop: true, width: 100, height: 23)
    {
    }

    public bool Multiline
    {
        get => _multiline;
        set
        {
            Application.VerifyUiThread();
            if (_multiline == value)
            {
                return;
            }

            _multiline = value;
            RecreateHandle();
        }
    }

    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            Application.VerifyUiThread();
            if (_wordWrap == value)
            {
                return;
            }

            _wordWrap = value;
            RecreateHandle();
        }
    }

    public ScrollBars ScrollBars
    {
        get => _scrollBars;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_scrollBars == value)
            {
                return;
            }

            _scrollBars = value;
            RecreateHandle();
        }
    }

    internal override string NativeClassName => "EDIT";

    internal override WINDOW_STYLE NativeStyle
    {
        get
        {
            WINDOW_STYLE style = base.NativeStyle | (WINDOW_STYLE)NativeConstants.ES_AUTOVSCROLL;

            if (!Multiline)
            {
                return style | (WINDOW_STYLE)NativeConstants.ES_AUTOHSCROLL;
            }

            style |= (WINDOW_STYLE)NativeConstants.ES_MULTILINE;
            if (!WordWrap)
            {
                style |= (WINDOW_STYLE)NativeConstants.ES_AUTOHSCROLL;
            }

            if ((ScrollBars & ScrollBars.Horizontal) == ScrollBars.Horizontal && !WordWrap)
            {
                style |= WINDOW_STYLE.WS_HSCROLL;
            }

            if ((ScrollBars & ScrollBars.Vertical) == ScrollBars.Vertical)
            {
                style |= WINDOW_STYLE.WS_VSCROLL;
            }

            return style;
        }
    }

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;
}
