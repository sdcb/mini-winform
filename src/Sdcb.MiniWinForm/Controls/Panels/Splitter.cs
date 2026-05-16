using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class Splitter : Control
{
    private const int DefaultThickness = 3;

    private int _minSize = 25;
    private int _minExtra = 25;
    private int _splitterThickness = DefaultThickness;
    private Control? _splitTarget;
    private int _anchorParentCoordinate;
    private int _initTargetSize;
    private int _splitSize = -1;
    private int _maxSize;

    public Splitter()
        : base(tabStop: false, width: DefaultThickness, height: DefaultThickness)
    {
        Dock = DockStyle.Left;
    }

    public int MinSize
    {
        get => _minSize;
        set => _minSize = Math.Max(0, value);
    }

    public int MinExtra
    {
        get => _minExtra;
        set => _minExtra = Math.Max(0, value);
    }

    public int SplitPosition
    {
        get => CalcSplitSize();
        set => ApplySplitPosition(value, raiseMoved: true);
    }

    public event SplitterEventHandler? SplitterMoving;

    public event SplitterEventHandler? SplitterMoved;

    internal override string NativeClassName => "STATIC";

    internal override WINDOW_STYLE NativeStyle =>
        WINDOW_STYLE.WS_CHILD |
        WINDOW_STYLE.WS_CLIPSIBLINGS |
        (WINDOW_STYLE)NativeConstants.SS_NOTIFY |
        (WINDOW_STYLE)NativeConstants.SS_OWNERDRAW;

    public new DockStyle Dock
    {
        get => base.Dock;
        set
        {
            if (value is not (DockStyle.Top or DockStyle.Bottom or DockStyle.Left or DockStyle.Right))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Splitter can only be docked to Top, Bottom, Left, or Right.");
            }

            base.Dock = value;
            if (IsVertical)
            {
                Width = _splitterThickness;
            }
            else
            {
                Height = _splitterThickness;
            }
        }
    }

    private bool IsVertical => Dock is DockStyle.Left or DockStyle.Right;

    internal override bool ProcessWindowMessage(uint message, WPARAM wParam, LPARAM lParam, out LRESULT result)
    {
        result = new LRESULT(0);
        switch (message)
        {
            case NativeConstants.WM_SETCURSOR:
                SetSplitterCursor();
                result = new LRESULT(1);
                return true;
            case NativeConstants.WM_LBUTTONDOWN:
                BeginSplit(GetPrimaryParentCoordinate(GetPrimaryCoordinate(lParam)));
                return true;
            case NativeConstants.WM_MOUSEMOVE:
                if (_splitTarget is not null && ((int)wParam.Value & NativeConstants.MK_LBUTTON) == NativeConstants.MK_LBUTTON)
                {
                    MoveSplit(GetPrimaryParentCoordinate(GetPrimaryCoordinate(lParam)));
                    return true;
                }

                break;
            case NativeConstants.WM_LBUTTONUP:
                EndSplit(accept: true);
                return true;
            case NativeConstants.WM_CANCELMODE:
                EndSplit(accept: false);
                return true;
        }

        return false;
    }

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        NativeDraw.DrawSplitter(drawItem.hDC, drawItem.rcItem, IsVertical, this);
        return true;
    }

    private int GetPrimaryCoordinate(LPARAM lParam)
    {
        int value = unchecked((int)lParam.Value);
        int x = unchecked((short)(value & 0xffff));
        int y = unchecked((short)((value >> 16) & 0xffff));
        return IsVertical ? x : y;
    }

    private int GetPrimaryParentCoordinate(int coordinate) => (IsVertical ? Left : Top) + coordinate;

    private void BeginSplit(int parentCoordinate)
    {
        SplitBounds bounds = CalcSplitBounds();
        if (bounds.Target is null || _minSize > _maxSize)
        {
            return;
        }

        _anchorParentCoordinate = parentCoordinate;
        _splitTarget = bounds.Target;
        _initTargetSize = GetTargetSize(_splitTarget);
        _splitSize = _initTargetSize;
        _ = PInvoke.SetCapture(NativeHandle);
    }

    private void MoveSplit(int parentCoordinate)
    {
        if (_splitTarget is null)
        {
            return;
        }

        int splitSize = ClampSplitSize(GetSplitSize(parentCoordinate));
        if (splitSize == _splitSize)
        {
            return;
        }

        _splitSize = splitSize;
        ApplySplitPosition(splitSize, raiseMoved: false);
        SplitterMoving?.Invoke(this, CreateEventArgs(parentCoordinate, splitSize));
    }

    private void EndSplit(bool accept)
    {
        if (_splitTarget is null)
        {
            return;
        }

        int finalSize = accept ? _splitSize : _initTargetSize;
        ApplySplitPosition(finalSize, raiseMoved: true);
        _splitTarget = null;
        _splitSize = -1;
        _anchorParentCoordinate = 0;
        _ = PInvoke.ReleaseCapture();
    }

    private void ApplySplitPosition(int value, bool raiseMoved)
    {
        SplitBounds bounds = CalcSplitBounds();
        if (bounds.Target is null)
        {
            _splitSize = -1;
            return;
        }

        int splitSize = ClampSplitSize(value);
        SetTargetSize(bounds.Target, splitSize);
        _splitSize = splitSize;
        if (raiseMoved)
        {
            SplitterMoved?.Invoke(this, CreateEventArgs(GetCurrentSplitParentCoordinate(splitSize), splitSize));
        }
    }

    private SplitBounds CalcSplitBounds()
    {
        Control? target = FindTarget();
        if (target is null || Parent is null)
        {
            return new SplitBounds(null);
        }

        Parent.GetClientSize(out _, out _, out int parentWidth, out int parentHeight);
        int dockWidth = 0;
        int dockHeight = 0;
        foreach (Control control in Parent.Controls)
        {
            if (control == target || !control.Visible)
            {
                continue;
            }

            switch (control.Dock)
            {
                case DockStyle.Left:
                case DockStyle.Right:
                    dockWidth += control.Width;
                    break;
                case DockStyle.Top:
                case DockStyle.Bottom:
                    dockHeight += control.Height;
                    break;
            }
        }

        _maxSize = IsVertical
            ? Math.Max(0, parentWidth - dockWidth - _minExtra)
            : Math.Max(0, parentHeight - dockHeight - _minExtra);

        return new SplitBounds(target);
    }

    private Control? FindTarget()
    {
        if (Parent is null)
        {
            return null;
        }

        foreach (Control target in Parent.Controls)
        {
            if (target == this || !target.Visible)
            {
                continue;
            }

            switch (Dock)
            {
                case DockStyle.Top when target.Top + target.Height == Top:
                case DockStyle.Bottom when target.Top == Top + Height:
                case DockStyle.Left when target.Left + target.Width == Left:
                case DockStyle.Right when target.Left == Left + Width:
                    return target;
            }
        }

        return null;
    }

    private int CalcSplitSize()
    {
        Control? target = FindTarget();
        return target is null ? -1 : GetTargetSize(target);
    }

    private int GetSplitSize(int parentCoordinate)
    {
        int delta = parentCoordinate - _anchorParentCoordinate;
        return Dock switch
        {
            DockStyle.Top or DockStyle.Left => _initTargetSize + delta,
            DockStyle.Bottom or DockStyle.Right => _initTargetSize - delta,
            _ => -1,
        };
    }

    private int ClampSplitSize(int value)
    {
        value = Math.Min(value, _maxSize);
        value = Math.Max(value, _minSize);
        return value;
    }

    private int GetTargetSize(Control target) => IsVertical ? target.Width : target.Height;

    private void SetTargetSize(Control target, int size)
    {
        if (IsVertical)
        {
            target.Width = size;
        }
        else
        {
            target.Height = size;
        }
    }

    private int GetCurrentSplitParentCoordinate(int splitSize)
    {
        if (_splitTarget is not null)
        {
            return (IsVertical ? _splitTarget.Left : _splitTarget.Top) + splitSize;
        }

        return IsVertical ? Left : Top;
    }

    private SplitterEventArgs CreateEventArgs(int parentCoordinate, int splitSize)
    {
        int x = IsVertical ? parentCoordinate : Left;
        int y = IsVertical ? Top : parentCoordinate;
        int splitX = IsVertical ? _splitTarget?.Left + splitSize ?? Left : Left;
        int splitY = IsVertical ? Top : _splitTarget?.Top + splitSize ?? Top;
        return new SplitterEventArgs(x, y, splitX, splitY);
    }

    private void SetSplitterCursor()
    {
        unsafe
        {
            ushort cursorId = (ushort)(IsVertical ? NativeConstants.IDC_SIZEWE : NativeConstants.IDC_SIZENS);
            HCURSOR cursor = PInvoke.LoadCursor(default, new PCWSTR((char*)cursorId));
            _ = PInvoke.SetCursor(cursor);
        }
    }

    private readonly record struct SplitBounds(Control? Target);
}