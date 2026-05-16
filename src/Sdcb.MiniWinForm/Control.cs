using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public abstract class Control : IWin32Window
{
    private Queue<ControlAsyncResult>? _threadCallbackList;
    private string _text = string.Empty;
    private int _left;
    private int _top;
    private int _width = 100;
    private int _height = 24;
    private int _specifiedLeft;
    private int _specifiedTop;
    private int _specifiedWidth = 100;
    private int _specifiedHeight = 24;
    private int _anchorLeft;
    private int _anchorTop;
    private int _anchorRight;
    private int _anchorBottom;
    private bool _anchorInfoInitialized;
    private bool _visible = true;
    private bool _enabled = true;
    private bool _tabStop;
    private bool _layoutInProgress;
    private DockStyle _dock;
    private AnchorStyles _anchor = AnchorStyles.Top | AnchorStyles.Left;
    private ContextMenuStrip? _contextMenuStrip;
    private Color _backColor = Color.Empty;
    private Color _foreColor = Color.Empty;
    private HBRUSH _backColorBrush;

    protected Control()
        : this(tabStop: true, width: 100, height: 24)
    {
    }

    protected Control(bool tabStop, int width, int height)
    {
        _tabStop = tabStop;
        _width = width;
        _height = height;
        _specifiedWidth = width;
        _specifiedHeight = height;
        Controls = new ControlCollection(this);
    }

    public string Text
    {
        get
        {
            Application.VerifyUiThread();
            if (IsHandleCreated)
            {
                _text = NativeControl.GetText(NativeHandle);
            }

            return _text;
        }
        set
        {
            Application.VerifyUiThread();
            _text = value ?? string.Empty;
            if (IsHandleCreated)
            {
                NativeControl.SetText(NativeHandle, _text);
            }
        }
    }

    public int Left
    {
        get => _left;
        set => SetSpecifiedBounds(value, Top, Width, Height);
    }

    public int Top
    {
        get => _top;
        set => SetSpecifiedBounds(Left, value, Width, Height);
    }

    public int Width
    {
        get => _width;
        set => SetSpecifiedBounds(Left, Top, value, Height);
    }

    public int Height
    {
        get => _height;
        set => SetSpecifiedBounds(Left, Top, Width, value);
    }

    public DockStyle Dock
    {
        get => _dock;
        set
        {
            Application.VerifyUiThread();
            ValidateDockStyle(value);
            if (_dock == value)
            {
                return;
            }

            _dock = value;
            _anchorInfoInitialized = false;
            if (_dock == DockStyle.None)
            {
                SetBoundsCore(_specifiedLeft, _specifiedTop, _specifiedWidth, _specifiedHeight);
                UpdateAnchorInfo();
            }

            Parent?.PerformLayout();
        }
    }

    public AnchorStyles Anchor
    {
        get => Dock == DockStyle.None ? _anchor : AnchorStyles.Top | AnchorStyles.Left;
        set
        {
            Application.VerifyUiThread();
            ValidateAnchorStyles(value);
            if (Dock == DockStyle.None && _anchor == value)
            {
                return;
            }

            _anchor = value;
            if (Dock != DockStyle.None)
            {
                _dock = DockStyle.None;
                SetBoundsCore(_specifiedLeft, _specifiedTop, _specifiedWidth, _specifiedHeight);
            }

            UpdateAnchorInfo();
            Parent?.PerformLayout();
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            Application.VerifyUiThread();
            _visible = value;
            if (IsHandleCreated)
            {
                NativeControl.Show(NativeHandle, _visible);
            }
        }
    }

    public bool Enabled
    {
        get
        {
            Application.VerifyUiThread();
            return _enabled && (Parent is null || Parent.Enabled);
        }
        set
        {
            Application.VerifyUiThread();
            bool oldValue = Enabled;
            _enabled = value;

            if (oldValue != value)
            {
                OnEnabledChanged(EventArgs.Empty);
            }
        }
    }

    public bool TabStop
    {
        get => _tabStop;
        set
        {
            Application.VerifyUiThread();
            _tabStop = value;
        }
    }

    public Color BackColor
    {
        get => _backColor;
        set
        {
            Application.VerifyUiThread();
            if (_backColor == value)
            {
                return;
            }

            _backColor = value;
            ResetBackColorBrushTree();
            InvalidateSelfAndChildren();
        }
    }

    public Color ForeColor
    {
        get => _foreColor;
        set
        {
            Application.VerifyUiThread();
            if (_foreColor == value)
            {
                return;
            }

            _foreColor = value;
            InvalidateSelfAndChildren();
        }
    }

    public Control? Parent { get; internal set; }

    public ControlCollection Controls { get; }

    public IntPtr Handle
    {
        get
        {
            Application.VerifyUiThread();
            if (!IsHandleCreated)
            {
                CreateHandle();
            }

            return NativeWindow.ToIntPtr(NativeHandle);
        }
    }

    public virtual ContextMenuStrip? ContextMenuStrip
    {
        get => _contextMenuStrip;
        set
        {
            Application.VerifyUiThread();
            if (_contextMenuStrip == value)
            {
                return;
            }

            _contextMenuStrip = value;
            ContextMenuStripChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ContextMenuStripChanged;

    public event EventHandler? EnabledChanged;

    public IAsyncResult BeginInvoke(Delegate method) => BeginInvoke(method, null);

    public IAsyncResult BeginInvoke(Action method) => BeginInvoke(method, null);

    public IAsyncResult BeginInvoke(Delegate method, params object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);

        Control marshaler = FindMarshalingControl();
        ControlAsyncResult asyncResult = new(method, args);

        lock (marshaler)
        {
            marshaler._threadCallbackList ??= [];
            marshaler._threadCallbackList.Enqueue(asyncResult);
        }

        if (!NativeControl.PostBeginInvoke(marshaler.NativeHandle))
        {
            throw new InvalidOperationException($"PostMessage failed. LastError={Marshal.GetLastPInvokeError()}.");
        }

        return asyncResult;
    }

    public object? EndInvoke(IAsyncResult asyncResult)
    {
        ArgumentNullException.ThrowIfNull(asyncResult);

        if (asyncResult is not ControlAsyncResult controlAsyncResult)
        {
            throw new ArgumentException("The IAsyncResult object was not returned by BeginInvoke.", nameof(asyncResult));
        }

        controlAsyncResult.AsyncWaitHandle.WaitOne();
        return controlAsyncResult.GetResult();
    }

    internal HWND NativeHandle { get; private set; }

    internal bool IsHandleCreated => NativeHandle != default;

    protected virtual Color DefaultBackColor => SystemColors.Control;

    protected virtual Color DefaultForeColor => SystemColors.ControlText;

    internal Color EffectiveBackColor => !_backColor.IsEmpty ? _backColor : Parent?.EffectiveBackColor ?? DefaultBackColor;

    internal Color EffectiveForeColor => !_foreColor.IsEmpty ? _foreColor : Parent?.EffectiveForeColor ?? DefaultForeColor;

    public Form? FindForm()
    {
        Application.VerifyUiThread();
        for (Control? control = this; control is not null; control = control.Parent)
        {
            if (control is Form form)
            {
                return form;
            }
        }

        return null;
    }

    private Control FindMarshalingControl()
    {
        for (Control? control = this; control is not null; control = control.Parent)
        {
            if (control.IsHandleCreated)
            {
                return control;
            }
        }

        throw new InvalidOperationException("No appropriate window handle can be found for marshaling the call.");
    }

    internal void InvokeMarshaledCallbacks()
    {
        while (true)
        {
            ControlAsyncResult? asyncResult;
            lock (this)
            {
                if (_threadCallbackList is null || _threadCallbackList.Count == 0)
                {
                    return;
                }

                asyncResult = _threadCallbackList.Dequeue();
            }

            asyncResult.Invoke();
        }
    }

    internal void AssignHandle(HWND handle)
    {
        NativeHandle = handle;
    }

    internal void RecreateHandle()
    {
        Application.VerifyUiThread();
        if (!IsHandleCreated)
        {
            return;
        }

        string currentText = Text;
        HWND previousHandle = NativeHandle;
        NativeHandle = default;
        _text = currentText;

        NativeControl.Destroy(previousHandle);
        CreateHandle();
    }

    internal unsafe LRESULT ApplyControlColor(WPARAM wParam, bool transparent)
    {
        HDC deviceContext = new((nint)wParam.Value);
        _ = PInvoke.SetTextColor(deviceContext, ToColorRef(Enabled ? EffectiveForeColor : SystemColors.GrayText));
        if (transparent)
        {
            _ = PInvoke.SetBkMode(deviceContext, BACKGROUND_MODE.TRANSPARENT);
        }
        else
        {
            _ = PInvoke.SetBkColor(deviceContext, ToColorRef(EffectiveBackColor));
        }

        return new LRESULT((nint)GetBackColorBrush().Value);
    }

    internal static COLORREF ToColorRef(Color color)
    {
        return new COLORREF((uint)(color.R | (color.G << 8) | (color.B << 16)));
    }

    private HBRUSH GetBackColorBrush()
    {
        if (_backColorBrush == default)
        {
            _backColorBrush = PInvoke.CreateSolidBrush(ToColorRef(EffectiveBackColor));
        }

        return _backColorBrush;
    }

    private void ResetBackColorBrushTree()
    {
        ResetBackColorBrush();
        foreach (Control child in Controls)
        {
            if (child._backColor.IsEmpty)
            {
                child.ResetBackColorBrushTree();
            }
        }
    }

    private unsafe void ResetBackColorBrush()
    {
        if (_backColorBrush != default)
        {
            _ = PInvoke.DeleteObject(new HGDIOBJ(_backColorBrush.Value));
            _backColorBrush = default;
        }
    }

    private void InvalidateSelfAndChildren()
    {
        if (IsHandleCreated)
        {
            NativeControl.Invalidate(NativeHandle);
        }

        foreach (Control child in Controls)
        {
            child.InvalidateSelfAndChildren();
        }
    }

    internal abstract string NativeClassName { get; }

    internal virtual WINDOW_STYLE NativeStyle
    {
        get
        {
            WINDOW_STYLE style = WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_CLIPSIBLINGS;
            if (TabStop)
            {
                style |= WINDOW_STYLE.WS_TABSTOP;
            }

            return style;
        }
    }

    internal virtual WINDOW_EX_STYLE NativeExStyle => 0;

    internal virtual void CreateHandle()
    {
        if (IsHandleCreated)
        {
            return;
        }

        if (Parent is null || !Parent.IsHandleCreated)
        {
            throw new InvalidOperationException("A child control needs a parent with a created handle.");
        }

        AssignHandle(NativeControl.CreateChild(this, Parent.NativeHandle, NativeClassName, Text, NativeStyle, NativeExStyle));
        NativeControl.Register(this, NativeHandle);
        NativeControl.ApplyDefaultFont(NativeHandle);
        NativeControl.Show(NativeHandle, Visible);
        PerformLayout();

        foreach (Control child in Controls)
        {
            child.CreateHandle();
        }
    }

    protected virtual void OnEnabledChanged(EventArgs e)
    {
        if (IsHandleCreated)
        {
            NativeControl.SetEnabled(NativeHandle, Enabled);
            NativeControl.Invalidate(NativeHandle);
        }

        EnabledChanged?.Invoke(this, e);

        foreach (Control child in Controls)
        {
            child.OnParentEnabledChanged(e);
        }
    }

    protected virtual void OnParentEnabledChanged(EventArgs e)
    {
        if (_enabled)
        {
            OnEnabledChanged(e);
        }
    }

    internal void NotifyParentEnabledChanged(EventArgs e)
    {
        OnParentEnabledChanged(e);
    }

    internal virtual void OnCommand(int notificationCode)
    {
    }

    internal virtual bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        _ = drawItem;
        return false;
    }

    internal virtual bool ProcessWindowMessage(uint message, WPARAM wParam, LPARAM lParam, out LRESULT result)
    {
        _ = message;
        _ = wParam;
        _ = lParam;
        result = new LRESULT(0);
        return false;
    }

    internal Control? FindFirstTabStopControl()
    {
        foreach (Control child in Controls)
        {
            if (child.Visible && child.Enabled && child.TabStop)
            {
                return child;
            }

            Control? nested = child.FindFirstTabStopControl();
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    internal bool ShowContextMenuFromMessage(LPARAM lParam)
    {
        ContextMenuStrip? contextMenu = FindContextMenuStrip();
        if (contextMenu is null)
        {
            return false;
        }

        GetContextMenuScreenLocation(lParam, out int screenX, out int screenY);
        contextMenu.ShowAtScreenLocation(this, screenX, screenY);
        return true;
    }

    private ContextMenuStrip? FindContextMenuStrip()
    {
        for (Control? control = this; control is not null; control = control.Parent)
        {
            if (control.ContextMenuStrip is not null)
            {
                return control.ContextMenuStrip;
            }
        }

        return null;
    }

    private void GetContextMenuScreenLocation(LPARAM lParam, out int screenX, out int screenY)
    {
        if (lParam.Value != -1)
        {
            int value = unchecked((int)lParam.Value);
            screenX = unchecked((short)(value & 0xffff));
            screenY = unchecked((short)((value >> 16) & 0xffff));
            return;
        }

        NativeMenu.GetWindowRect(NativeHandle, out int left, out int top, out int right, out int bottom);
        screenX = left + ((right - left) / 2);
        screenY = top + ((bottom - top) / 2);
    }

    internal void PerformLayout()
    {
        Application.VerifyUiThread();
        if (_layoutInProgress)
        {
            return;
        }

        _layoutInProgress = true;
        try
        {
            GetClientSize(out int left, out int top, out int width, out int height);
            int clientWidth = width;
            int clientHeight = height;

            for (int index = Controls.Count - 1; index >= 0; index--)
            {
                Control child = Controls[index];
                if (!child.Visible || child.Dock == DockStyle.None)
                {
                    continue;
                }

                switch (child.Dock)
                {
                    case DockStyle.Top:
                    {
                        int childHeight = Math.Min(child._specifiedHeight, height);
                        child.SetBoundsFromLayout(left, top, width, childHeight);
                        top += child.Height;
                        height -= child.Height;
                        break;
                    }
                    case DockStyle.Bottom:
                    {
                        int childHeight = Math.Min(child._specifiedHeight, height);
                        child.SetBoundsFromLayout(left, top + height - childHeight, width, childHeight);
                        height -= child.Height;
                        break;
                    }
                    case DockStyle.Left:
                    {
                        int childWidth = Math.Min(child._specifiedWidth, width);
                        child.SetBoundsFromLayout(left, top, childWidth, height);
                        left += child.Width;
                        width -= child.Width;
                        break;
                    }
                    case DockStyle.Right:
                    {
                        int childWidth = Math.Min(child._specifiedWidth, width);
                        child.SetBoundsFromLayout(left + width - childWidth, top, childWidth, height);
                        width -= child.Width;
                        break;
                    }
                    case DockStyle.Fill:
                        child.SetBoundsFromLayout(left, top, width, height);
                        break;
                }
            }

            for (int index = 0; index < Controls.Count; index++)
            {
                Control child = Controls[index];
                if (!child.Visible || child.Dock != DockStyle.None)
                {
                    continue;
                }

                child.EnsureAnchorInfo();
                child.SetBoundsFromLayout(child.GetAnchoredLeft(clientWidth), child.GetAnchoredTop(clientHeight), child.GetAnchoredWidth(clientWidth), child.GetAnchoredHeight(clientHeight));
            }
        }
        finally
        {
            _layoutInProgress = false;
        }
    }

    internal virtual void GetClientSize(out int left, out int top, out int width, out int height)
    {
        left = 0;
        top = 0;
        if (IsHandleCreated && PInvoke.GetClientRect(NativeHandle, out RECT rect))
        {
            width = Math.Max(0, rect.right - rect.left);
            height = Math.Max(0, rect.bottom - rect.top);
            return;
        }

        width = Width;
        height = Height;
    }

    private void SetSpecifiedBounds(int left, int top, int width, int height)
    {
        Application.VerifyUiThread();
        _specifiedLeft = left;
        _specifiedTop = top;
        _specifiedWidth = Math.Max(0, width);
        _specifiedHeight = Math.Max(0, height);

        if (Dock == DockStyle.None || Parent is null)
        {
            SetBoundsCore(left, top, width, height);
            UpdateAnchorInfo();
            return;
        }

        Parent.PerformLayout();
    }

    private void SetBoundsFromLayout(int left, int top, int width, int height)
    {
        SetBoundsCore(left, top, width, height);
    }

    private void EnsureAnchorInfo()
    {
        if (!_anchorInfoInitialized)
        {
            UpdateAnchorInfo();
        }
    }

    private void UpdateAnchorInfo()
    {
        if (Parent is null)
        {
            _anchorInfoInitialized = false;
            return;
        }

        Parent.GetClientSize(out _, out _, out int parentWidth, out int parentHeight);
        _anchorLeft = Left;
        _anchorTop = Top;
        _anchorRight = parentWidth - (Left + Width);
        _anchorBottom = parentHeight - (Top + Height);
        _anchorInfoInitialized = true;
    }

    private int GetAnchoredLeft(int parentWidth)
    {
        bool leftAnchored = IsAnchored(AnchorStyles.Left);
        bool rightAnchored = IsAnchored(AnchorStyles.Right);

        if (leftAnchored)
        {
            return _anchorLeft;
        }

        if (rightAnchored)
        {
            return parentWidth - Width - _anchorRight;
        }

        return _anchorLeft + ((parentWidth - (_anchorLeft + _anchorRight + Width)) / 2);
    }

    private int GetAnchoredTop(int parentHeight)
    {
        bool topAnchored = IsAnchored(AnchorStyles.Top);
        bool bottomAnchored = IsAnchored(AnchorStyles.Bottom);

        if (topAnchored)
        {
            return _anchorTop;
        }

        if (bottomAnchored)
        {
            return parentHeight - Height - _anchorBottom;
        }

        return _anchorTop + ((parentHeight - (_anchorTop + _anchorBottom + Height)) / 2);
    }

    private int GetAnchoredWidth(int parentWidth)
    {
        return IsAnchored(AnchorStyles.Left) && IsAnchored(AnchorStyles.Right)
            ? parentWidth - (_anchorLeft + _anchorRight)
            : Width;
    }

    private int GetAnchoredHeight(int parentHeight)
    {
        return IsAnchored(AnchorStyles.Top) && IsAnchored(AnchorStyles.Bottom)
            ? parentHeight - (_anchorTop + _anchorBottom)
            : Height;
    }

    private bool IsAnchored(AnchorStyles anchor) => (Anchor & anchor) == anchor;

    private void SetBoundsCore(int left, int top, int width, int height)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        bool sizeChanged = _width != width || _height != height;

        _left = left;
        _top = top;
        _width = width;
        _height = height;

        if (IsHandleCreated)
        {
            NativeControl.Move(NativeHandle, Left, Top, Width, Height);
        }

        if (sizeChanged)
        {
            PerformLayout();
        }
    }

    private static void ValidateDockStyle(DockStyle value)
    {
        if (value is < DockStyle.None or > DockStyle.Fill)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid DockStyle value.");
        }
    }

    private static void ValidateAnchorStyles(AnchorStyles value)
    {
        const AnchorStyles all = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        if ((value & ~all) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid AnchorStyles value.");
        }
    }
}

internal sealed class ControlAsyncResult : IAsyncResult
{
    private readonly Delegate _method;
    private readonly object?[]? _args;
    private readonly ManualResetEvent _asyncWaitHandle = new(false);
    private object? _result;
    private Exception? _exception;

    internal ControlAsyncResult(Delegate method, object?[]? args)
    {
        _method = method;
        _args = args;
    }

    public object? AsyncState => null;

    public WaitHandle AsyncWaitHandle => _asyncWaitHandle;

    public bool CompletedSynchronously => false;

    public bool IsCompleted { get; private set; }

    internal void Invoke()
    {
        try
        {
            _result = _method.DynamicInvoke(_args);
        }
        catch (Exception exception)
        {
            _exception = exception.InnerException ?? exception;
        }
        finally
        {
            IsCompleted = true;
            _asyncWaitHandle.Set();
        }
    }

    internal object? GetResult()
    {
        if (_exception is not null)
        {
            throw _exception;
        }

        return _result;
    }
}

internal static unsafe class NativeControl
{
    private static readonly Dictionary<nint, Control> ControlsByHandle = [];
    private static readonly Dictionary<nint, nint> OriginalWindowProcedures = [];
    private static readonly WNDPROC SharedWindowProcedure = WindowProcedure;
    private static int _nextControlId = NativeConstants.DefaultControlId;

    internal static HWND CreateChild(
        Control owner,
        HWND parent,
        string className,
        string text,
        WINDOW_STYLE style,
        WINDOW_EX_STYLE exStyle)
    {
        WINDOW_STYLE finalStyle = style | (owner.Visible ? WINDOW_STYLE.WS_VISIBLE : 0);
        if (!owner.Enabled)
        {
            finalStyle |= WINDOW_STYLE.WS_DISABLED;
        }

        fixed (char* classNamePtr = className)
        fixed (char* textPtr = text)
        {
            HWND handle = PInvoke.CreateWindowEx(
                exStyle,
                new PCWSTR(classNamePtr),
                new PCWSTR(textPtr),
                finalStyle,
                owner.Left,
                owner.Top,
                owner.Width,
                owner.Height,
                parent,
                new HMENU(_nextControlId++),
                Application.Instance,
                null);

            if (handle == default)
            {
                throw new InvalidOperationException($"CreateWindowEx failed for '{className}'. LastError={Marshal.GetLastPInvokeError()}.");
            }

            return handle;
        }
    }

    internal static void Register(Control control, HWND handle)
    {
        ControlsByHandle[(nint)handle.Value] = control;
        Subclass(handle);
    }

    internal static void Destroy(HWND handle)
    {
        nint handleValue = (nint)handle.Value;
        if (!PInvoke.DestroyWindow(handle))
        {
            throw new InvalidOperationException($"DestroyWindow failed. LastError={Marshal.GetLastPInvokeError()}.");
        }

        ControlsByHandle.Remove(handleValue);
        OriginalWindowProcedures.Remove(handleValue);
    }

    private static void Subclass(HWND handle)
    {
        nint handleValue = (nint)handle.Value;
        if (OriginalWindowProcedures.ContainsKey(handleValue))
        {
            return;
        }

        nint previousProcedure = PInvoke.SetWindowLongPtr(
            handle,
            (WINDOW_LONG_PTR_INDEX)NativeConstants.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(SharedWindowProcedure));

        if (previousProcedure != 0)
        {
            OriginalWindowProcedures[handleValue] = previousProcedure;
        }
    }

    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        Control? owner = FromHandle(window);
        if (owner is not null && owner.ProcessWindowMessage(message, wParam, lParam, out LRESULT result))
        {
            return result;
        }

        switch (message)
        {
            case NativeConstants.WM_BEGININVOKE:
                owner?.InvokeMarshaledCallbacks();
                return new LRESULT(0);
            case NativeConstants.WM_COMMAND:
                DispatchCommand(wParam, lParam);
                return new LRESULT(0);
            case NativeConstants.WM_SIZE:
                owner?.PerformLayout();
                return CallOriginalWindowProcedure(window, message, wParam, lParam);
            case NativeConstants.WM_CONTEXTMENU:
                if (owner is not null && owner.ShowContextMenuFromMessage(lParam))
                {
                    return new LRESULT(0);
                }

                return CallOriginalWindowProcedure(window, message, wParam, lParam);
            case NativeConstants.WM_DRAWITEM:
                return DispatchDrawItem(lParam);
            case NativeConstants.WM_CTLCOLOREDIT:
                return Form.ApplyEditControlColor(wParam, lParam);
            case NativeConstants.WM_CTLCOLORBTN:
            case NativeConstants.WM_CTLCOLORSTATIC:
                return Form.ApplyStaticControlColor(wParam, lParam);
            default:
                return CallOriginalWindowProcedure(window, message, wParam, lParam);
        }
    }

    internal static bool PostBeginInvoke(HWND handle)
    {
        return PInvoke.PostMessage(handle, NativeConstants.WM_BEGININVOKE, new WPARAM(0), new LPARAM(0));
    }

    private static LRESULT CallOriginalWindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (!OriginalWindowProcedures.TryGetValue((nint)window.Value, out nint previousProcedure))
        {
            return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }

        WNDPROC windowProcedure = Marshal.GetDelegateForFunctionPointer<WNDPROC>(previousProcedure);
        return PInvoke.CallWindowProc(windowProcedure, window, message, wParam, lParam);
    }

    internal static Control? FromHandle(LPARAM lParam)
    {
        return ControlsByHandle.TryGetValue(lParam.Value, out Control? control) ? control : null;
    }

    internal static Control? FromHandle(HWND handle)
    {
        return ControlsByHandle.TryGetValue((nint)handle.Value, out Control? control) ? control : null;
    }

    internal static bool TryGetControlAtScreenPoint(LPARAM lParam, out Control control)
    {
        control = null!;
        if (lParam.Value == -1)
        {
            return false;
        }

        int value = unchecked((int)lParam.Value);
        int screenX = unchecked((short)(value & 0xffff));
        int screenY = unchecked((short)((value >> 16) & 0xffff));
        int bestArea = int.MaxValue;

        foreach ((nint handleValue, Control candidate) in ControlsByHandle)
        {
            if (!candidate.IsHandleCreated || !PInvoke.GetWindowRect(new HWND(handleValue), out RECT rect))
            {
                continue;
            }

            if (screenX < rect.left || screenX >= rect.right || screenY < rect.top || screenY >= rect.bottom)
            {
                continue;
            }

            int area = (rect.right - rect.left) * (rect.bottom - rect.top);
            if (area < bestArea)
            {
                bestArea = area;
                control = candidate;
            }
        }

        return control is not null;
    }

    internal static void DispatchCommand(WPARAM wParam, LPARAM lParam)
    {
        Control? control = FromHandle(lParam);
        if (control is null)
        {
            return;
        }

        int notificationCode = unchecked((short)((wParam.Value >> 16) & 0xffff));
        control.OnCommand(notificationCode);
    }

    internal static LRESULT DispatchDrawItem(LPARAM lParam)
    {
        DRAWITEMSTRUCT drawItem = *(DRAWITEMSTRUCT*)lParam.Value;
        Control? control = FromHandle(drawItem.hwndItem);
        return control is not null && control.DrawItem(drawItem)
            ? new LRESULT(1)
            : new LRESULT(0);
    }

    internal static string GetText(HWND handle)
    {
        int length = PInvoke.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[length + 1];
        _ = PInvoke.GetWindowText(handle, buffer);

        return new string(buffer, 0, length);
    }

    internal static void SetText(HWND handle, string text)
    {
        if (!PInvoke.SetWindowText(handle, text))
        {
            throw new InvalidOperationException($"SetWindowText failed. LastError={Marshal.GetLastPInvokeError()}.");
        }
    }

    internal static void Move(HWND handle, int left, int top, int width, int height)
    {
        if (!PInvoke.MoveWindow(handle, left, top, width, height, true))
        {
            throw new InvalidOperationException($"MoveWindow failed. LastError={Marshal.GetLastPInvokeError()}.");
        }
    }

    internal static void Show(HWND handle, bool visible)
    {
        PInvoke.ShowWindow(handle, visible ? SHOW_WINDOW_CMD.SW_SHOW : SHOW_WINDOW_CMD.SW_HIDE);
    }

    internal static void SetEnabled(HWND handle, bool enabled)
    {
        if (!PInvoke.EnableWindow(handle, enabled))
        {
            _ = Marshal.GetLastPInvokeError();
        }
    }

    internal static void Focus(Control control)
    {
        _ = PInvoke.SetFocus(control.NativeHandle);
        Invalidate(control.NativeHandle);
        if (control is TextBox)
        {
            _ = PInvoke.SendMessage(
                control.NativeHandle,
                NativeConstants.EM_SETSEL,
                new WPARAM(0),
                new LPARAM(NativeConstants.TextSelectionEnd));
        }
    }

    internal static void ApplyDefaultFont(HWND handle)
    {
        HFONT font = NativeFont.MessageFont;
        _ = PInvoke.SendMessage(handle, NativeConstants.WM_SETFONT, new WPARAM((nuint)font.Value), new LPARAM(1));
    }

    internal static HFONT GetFont(HWND handle)
    {
        LRESULT result = PInvoke.SendMessage(handle, NativeConstants.WM_GETFONT, new WPARAM(0), new LPARAM(0));
        return new HFONT(result.Value);
    }

    internal static bool GetCheckState(HWND handle)
    {
        LRESULT result = PInvoke.SendMessage(handle, NativeConstants.BM_GETCHECK, new WPARAM(0), new LPARAM(0));
        return result.Value == NativeConstants.BST_CHECKED;
    }

    internal static void SetCheckState(HWND handle, bool checkedState)
    {
        nuint value = (nuint)(checkedState ? NativeConstants.BST_CHECKED : NativeConstants.BST_UNCHECKED);
        _ = PInvoke.SendMessage(handle, NativeConstants.BM_SETCHECK, new WPARAM(value), new LPARAM(0));
    }

    internal static void Invalidate(HWND handle)
    {
        _ = PInvoke.InvalidateRect(handle, lpRect: null, bErase: true);
        PInvoke.UpdateWindow(handle);
    }
}

internal static unsafe class NativeFont
{
    private static HFONT _messageFont;

    internal static HFONT MessageFont
    {
        get
        {
            if (_messageFont == default)
            {
                NONCLIENTMETRICSW metrics = new()
                {
                    cbSize = (uint)Marshal.SizeOf<NONCLIENTMETRICSW>(),
                };

                if (!PInvoke.SystemParametersInfo(
                    (SYSTEM_PARAMETERS_INFO_ACTION)NativeConstants.SPI_GETNONCLIENTMETRICS,
                    metrics.cbSize,
                    &metrics,
                    0))
                {
                    throw new InvalidOperationException($"SystemParametersInfo(SPI_GETNONCLIENTMETRICS) failed. LastError={Marshal.GetLastPInvokeError()}.");
                }

                _messageFont = PInvoke.CreateFontIndirect(&metrics.lfMessageFont);

                if (_messageFont == default)
                {
                    throw new InvalidOperationException($"CreateFontIndirect failed. LastError={Marshal.GetLastPInvokeError()}.");
                }
            }

            return _messageFont;
        }
    }
}

internal static unsafe class NativeDraw
{
    private static HTHEME _buttonTheme;
    private static HTHEME _statusTheme;

    private static HTHEME ButtonTheme
    {
        get
        {
            if (_buttonTheme == default)
            {
                _buttonTheme = OpenThemeData("BUTTON");
            }

            return _buttonTheme;
        }
    }

    private static HTHEME StatusTheme
    {
        get
        {
            if (_statusTheme == default)
            {
                _statusTheme = OpenThemeData("STATUS");
            }

            return _statusTheme;
        }
    }

    internal static void FillControlBackground(HDC deviceContext, RECT bounds, Control? control = null)
    {
        HBRUSH brush = control is null
            ? PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)NativeConstants.COLOR_BTNFACE)
            : PInvoke.CreateSolidBrush(Control.ToColorRef(control.EffectiveBackColor));
        _ = PInvoke.FillRect(deviceContext, &bounds, brush);
        if (control is not null)
        {
            _ = PInvoke.DeleteObject(new HGDIOBJ(brush.Value));
        }
    }

    internal static void DrawSplitter(HDC deviceContext, RECT bounds, bool vertical, Control splitter)
    {
        FillControlBackground(deviceContext, bounds, splitter);

        int width = Math.Max(0, bounds.right - bounds.left);
        int height = Math.Max(0, bounds.bottom - bounds.top);
        if (width == 0 || height == 0)
        {
            return;
        }

        int firstLine = vertical
            ? bounds.left + Math.Max(0, (width / 2) - 1)
            : bounds.top + Math.Max(0, (height / 2) - 1);
        int secondLine = firstLine + 1;

        HBRUSH shadow = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)NativeConstants.COLOR_3DSHADOW);
        HBRUSH highlight = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)NativeConstants.COLOR_3DHIGHLIGHT);

        RECT first = vertical
            ? new RECT { left = firstLine, top = bounds.top, right = firstLine + 1, bottom = bounds.bottom }
            : new RECT { left = bounds.left, top = firstLine, right = bounds.right, bottom = firstLine + 1 };
        RECT second = vertical
            ? new RECT { left = secondLine, top = bounds.top, right = secondLine + 1, bottom = bounds.bottom }
            : new RECT { left = bounds.left, top = secondLine, right = bounds.right, bottom = secondLine + 1 };

        _ = PInvoke.FillRect(deviceContext, &first, shadow);
        _ = PInvoke.FillRect(deviceContext, &second, highlight);
    }

    internal static void DrawCheckBox(DRAWITEMSTRUCT drawItem, string text, bool isChecked, bool isRadioButton, Control control)
    {
        RECT bounds = drawItem.rcItem;
        FillControlBackground(drawItem.hDC, bounds, control);
        _ = PInvoke.SetBkMode(drawItem.hDC, BACKGROUND_MODE.TRANSPARENT);

        int height = bounds.bottom - bounds.top;
        int glyphTop = bounds.top + Math.Max(0, (height - NativeConstants.CheckGlyphSize) / 2);
        RECT glyph = new()
        {
            left = bounds.left,
            top = glyphTop,
            right = bounds.left + NativeConstants.CheckGlyphSize,
            bottom = glyphTop + NativeConstants.CheckGlyphSize,
        };

        if (!DrawThemedCheckGlyph(drawItem, glyph, isChecked, isRadioButton))
        {
            DrawClassicCheckGlyph(drawItem, glyph, isChecked, isRadioButton);
        }

        RECT textBounds = bounds;
        textBounds.left = glyph.right + NativeConstants.CheckTextGap;
        COLORREF previousTextColor = SetDrawItemTextColor(drawItem, control);
        try
        {
            DrawText(
                drawItem.hDC,
                text,
                ref textBounds,
                DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }
        finally
        {
            _ = PInvoke.SetTextColor(drawItem.hDC, previousTextColor);
        }
    }

    internal static void DrawButton(DRAWITEMSTRUCT drawItem, string text, HWND handle, Control control)
    {
        RECT bounds = drawItem.rcItem;
        RECT textBounds = bounds;
        textBounds.left += 3;
        textBounds.top += 3;
        textBounds.right -= 3;
        textBounds.bottom -= 3;

        if (!DrawThemedButton(drawItem, bounds, handle))
        {
            DrawClassicButton(drawItem, bounds);
        }

        _ = PInvoke.SetBkMode(drawItem.hDC, BACKGROUND_MODE.TRANSPARENT);
        HFONT font = NativeControl.GetFont(handle);
        HGDIOBJ previousFont = default;
        if (font != default)
        {
            previousFont = PInvoke.SelectObject(drawItem.hDC, new HGDIOBJ(font.Value));
        }

        COLORREF previousTextColor = SetDrawItemTextColor(drawItem, control);
        try
        {
            DrawText(
                drawItem.hDC,
                text,
                ref textBounds,
                DRAW_TEXT_FORMAT.DT_SINGLELINE |
                DRAW_TEXT_FORMAT.DT_CENTER |
                DRAW_TEXT_FORMAT.DT_VCENTER |
                DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }
        finally
        {
            _ = PInvoke.SetTextColor(drawItem.hDC, previousTextColor);
            if (previousFont != default)
            {
                _ = PInvoke.SelectObject(drawItem.hDC, previousFont);
            }
        }
    }

    internal static void DrawStatusStrip(DRAWITEMSTRUCT drawItem, StatusStrip statusStrip)
    {
        RECT bounds = drawItem.rcItem;
        DrawStatusStripBackground(drawItem.hDC, bounds);
        DrawStatusStripSizingGrip(drawItem.hDC, bounds);

        int itemCount = statusStrip.Items.Count;
        if (itemCount == 0)
        {
            return;
        }

        HFONT font = NativeControl.GetFont(statusStrip.NativeHandle);
        HGDIOBJ previousFont = default;
        if (font != default)
        {
            previousFont = PInvoke.SelectObject(drawItem.hDC, new HGDIOBJ(font.Value));
        }

        try
        {
            ToolStripStatusLabel[] labels = statusStrip.Items.StatusLabels.ToArray();
            int[] widths = new int[labels.Length];
            int springCount = 0;
            int fixedWidth = 0;
            int availableWidth = Math.Max(0, bounds.right - bounds.left - NativeConstants.StatusStripGripPaddingWidth - 1);

            for (int index = 0; index < labels.Length; index++)
            {
                ToolStripStatusLabel label = labels[index];
                if (label.Spring)
                {
                    springCount++;
                    continue;
                }

                widths[index] = GetStatusLabelPreferredWidth(drawItem.hDC, label);
                fixedWidth += widths[index];
            }

            int remainingWidth = Math.Max(0, availableWidth - fixedWidth);
            int springWidth = springCount == 0 ? 0 : remainingWidth / springCount;
            int springRemainder = springCount == 0 ? 0 : remainingWidth % springCount;

            int left = bounds.left + 4;
            int top = bounds.top + 1;
            int bottom = bounds.bottom - 1;
            for (int index = 0; index < labels.Length && left < bounds.right; index++)
            {
                ToolStripStatusLabel label = labels[index];
                int width = widths[index];
                if (label.Spring)
                {
                    width = springWidth;
                    if (springRemainder > 0)
                    {
                        width++;
                        springRemainder--;
                    }
                }

                int availableItemWidth = Math.Max(0, bounds.right - NativeConstants.StatusStripGripPaddingWidth - left);
                if (!label.Spring && width > availableItemWidth)
                {
                    break;
                }

                width = Math.Min(width, availableItemWidth);
                RECT itemBounds = new()
                {
                    left = left,
                    top = top,
                    right = left + width,
                    bottom = bottom,
                };

                DrawStatusLabel(drawItem.hDC, label, itemBounds);
                left += width;
            }
        }
        finally
        {
            if (previousFont != default)
            {
                _ = PInvoke.SelectObject(drawItem.hDC, previousFont);
            }
        }
    }

    internal static void DrawPictureBox(DRAWITEMSTRUCT drawItem, PictureBox pictureBox)
    {
        RECT bounds = drawItem.rcItem;
        FillControlBackground(drawItem.hDC, bounds);

        Image? image = pictureBox.GetImage();
        if (image is null)
        {
            return;
        }

        pictureBox.GetImageRectangle(out int left, out int top, out int width, out int height);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        NativeImage.DrawBitmap(drawItem.hDC, image, left, top, width, height);
    }

    internal static void DrawCheckedListBoxItem(DRAWITEMSTRUCT drawItem, string text, bool isChecked, bool enabled)
    {
        RECT bounds = drawItem.rcItem;
        bool selected = ((int)drawItem.itemState & NativeConstants.ODS_SELECTED) != 0;

        int backgroundColor = selected ? NativeConstants.COLOR_HIGHLIGHT : NativeConstants.COLOR_WINDOW;
        HBRUSH backgroundBrush = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)backgroundColor);
        _ = PInvoke.FillRect(drawItem.hDC, &bounds, backgroundBrush);
        _ = PInvoke.SetBkMode(drawItem.hDC, BACKGROUND_MODE.TRANSPARENT);

        int height = bounds.bottom - bounds.top;
        int glyphTop = bounds.top + Math.Max(0, (height - NativeConstants.CheckGlyphSize) / 2);
        RECT glyph = new()
        {
            left = bounds.left + 2,
            top = glyphTop,
            right = bounds.left + 2 + NativeConstants.CheckGlyphSize,
            bottom = glyphTop + NativeConstants.CheckGlyphSize,
        };

        uint state = NativeConstants.DFCS_BUTTONCHECK;
        if (isChecked)
        {
            state |= NativeConstants.DFCS_CHECKED;
        }

        if (!enabled)
        {
            state |= NativeConstants.DFCS_INACTIVE;
        }

        _ = PInvoke.DrawFrameControl(drawItem.hDC, ref glyph, NativeConstants.DFC_BUTTON, state);

        RECT textBounds = bounds;
        textBounds.left = glyph.right + NativeConstants.CheckTextGap;
        textBounds.right -= 3;

        int textColor = !enabled
            ? NativeConstants.COLOR_GRAYTEXT
            : selected ? NativeConstants.COLOR_HIGHLIGHTTEXT : NativeConstants.COLOR_WINDOWTEXT;
        COLORREF previousTextColor = PInvoke.SetTextColor(drawItem.hDC, new COLORREF((uint)PInvoke.GetSysColor((SYS_COLOR_INDEX)textColor)));
        try
        {
            DrawText(
                drawItem.hDC,
                text,
                ref textBounds,
                DRAW_TEXT_FORMAT.DT_SINGLELINE |
                DRAW_TEXT_FORMAT.DT_VCENTER |
                DRAW_TEXT_FORMAT.DT_END_ELLIPSIS |
                DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }
        finally
        {
            _ = PInvoke.SetTextColor(drawItem.hDC, previousTextColor);
        }
    }

    private static int GetStatusLabelPreferredWidth(HDC deviceContext, ToolStripStatusLabel label)
    {
        if (!label.AutoSize)
        {
            return label.Width;
        }

        RECT textBounds = new()
        {
            left = 0,
            top = 0,
            right = 4096,
            bottom = 100,
        };

        DrawText(
            deviceContext,
            label.Text,
            ref textBounds,
            DRAW_TEXT_FORMAT.DT_SINGLELINE | (DRAW_TEXT_FORMAT)NativeConstants.DT_CALCRECT | DRAW_TEXT_FORMAT.DT_NOPREFIX);

        return Math.Max(0, textBounds.right - textBounds.left) + 12;
    }

    private static void DrawStatusLabel(HDC deviceContext, ToolStripStatusLabel label, RECT bounds)
    {
        if (bounds.right <= bounds.left)
        {
            return;
        }

        DrawStatusLabelBorder(deviceContext, label.BorderSides, bounds);

        RECT textBounds = bounds;
        textBounds.left += 6;
        textBounds.right -= 6;
        _ = PInvoke.SetBkMode(deviceContext, BACKGROUND_MODE.TRANSPARENT);

        uint textColor = (uint)PInvoke.GetSysColor((SYS_COLOR_INDEX)(label.Enabled ? NativeConstants.COLOR_WINDOWTEXT : NativeConstants.COLOR_GRAYTEXT));
        COLORREF previousTextColor = PInvoke.SetTextColor(deviceContext, new COLORREF(textColor));
        try
        {
            DrawText(
                deviceContext,
                label.Text,
                ref textBounds,
                DRAW_TEXT_FORMAT.DT_SINGLELINE |
                DRAW_TEXT_FORMAT.DT_CENTER |
                DRAW_TEXT_FORMAT.DT_VCENTER |
                DRAW_TEXT_FORMAT.DT_END_ELLIPSIS |
                DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }
        finally
        {
            _ = PInvoke.SetTextColor(deviceContext, previousTextColor);
        }
    }

    private static void DrawStatusStripBackground(HDC deviceContext, RECT bounds)
    {
        if (DrawThemedStatusStripBackground(deviceContext, bounds))
        {
            return;
        }

        FillControlBackground(deviceContext, bounds);
        DrawClassicStatusStripTopLine(deviceContext, bounds);
    }

    private static bool DrawThemedStatusStripBackground(HDC deviceContext, RECT bounds)
    {
        HTHEME theme = StatusTheme;
        if (theme == default)
        {
            return false;
        }

        RECT themedBounds = bounds;
        themedBounds.right = Math.Max(themedBounds.left, themedBounds.right - 1);
        themedBounds.bottom = Math.Max(themedBounds.top, themedBounds.bottom - 1);
        return PInvoke.DrawThemeBackground(theme, deviceContext, 0, 0, &themedBounds, null).Succeeded;
    }

    private static void DrawClassicStatusStripTopLine(HDC deviceContext, RECT bounds)
    {
        HBRUSH brush = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)NativeConstants.COLOR_3DHIGHLIGHT);
        FillStatusLine(deviceContext, brush, bounds.left, bounds.top, bounds.right, bounds.top + 1);
    }

    private static void DrawStatusStripSizingGrip(HDC deviceContext, RECT bounds)
    {
        int gripHeight = Math.Min(NativeConstants.StatusStripGripHeight, bounds.bottom - bounds.top);
        if (gripHeight <= 0)
        {
            return;
        }

        int originX = bounds.right - NativeConstants.StatusStripGripCornerOffset;
        int originY = bounds.bottom - NativeConstants.StatusStripGripCornerOffset;
        HBRUSH highlightBrush = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)NativeConstants.COLOR_3DHIGHLIGHT);
        HBRUSH shadowBrush = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)NativeConstants.COLOR_GRAYTEXT);

        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 4, 12);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 0, 16);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 8, 4);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 4, 8);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 0, 12);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 8, 0);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 4, 4);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 0, 8);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 4, 0);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 0, 4);
        DrawStatusStripGripDot(deviceContext, highlightBrush, shadowBrush, originX, originY, 1, 1);
    }

    private static void DrawStatusStripGripDot(
        HDC deviceContext,
        HBRUSH highlightBrush,
        HBRUSH shadowBrush,
        int originX,
        int originY,
        int offsetX,
        int offsetY)
    {
        int left = originX - offsetX - 2;
        int top = originY - offsetY - 2;
        FillStatusLine(deviceContext, highlightBrush, left - 1, top - 1, left + 1, top + 1);
        FillStatusLine(deviceContext, shadowBrush, left + 1, top + 1, left + 3, top + 3);
    }

    private static void DrawStatusLabelBorder(HDC deviceContext, ToolStripStatusLabelBorderSides borderSides, RECT bounds)
    {
        if (borderSides == ToolStripStatusLabelBorderSides.None)
        {
            return;
        }

        HBRUSH brush = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)NativeConstants.COLOR_3DSHADOW);
        if ((borderSides & ToolStripStatusLabelBorderSides.Left) != 0)
        {
            FillStatusLine(deviceContext, brush, bounds.left, bounds.top, bounds.left + 1, bounds.bottom);
        }

        if ((borderSides & ToolStripStatusLabelBorderSides.Top) != 0)
        {
            FillStatusLine(deviceContext, brush, bounds.left, bounds.top, bounds.right, bounds.top + 1);
        }

        if ((borderSides & ToolStripStatusLabelBorderSides.Right) != 0)
        {
            FillStatusLine(deviceContext, brush, bounds.right - 1, bounds.top, bounds.right, bounds.bottom);
        }

        if ((borderSides & ToolStripStatusLabelBorderSides.Bottom) != 0)
        {
            FillStatusLine(deviceContext, brush, bounds.left, bounds.bottom - 1, bounds.right, bounds.bottom);
        }
    }

    private static void FillStatusLine(HDC deviceContext, HBRUSH brush, int left, int top, int right, int bottom)
    {
        RECT line = new()
        {
            left = left,
            top = top,
            right = right,
            bottom = bottom,
        };

        _ = PInvoke.FillRect(deviceContext, &line, brush);
    }

    internal static void DrawGroupBox(DRAWITEMSTRUCT drawItem, string text, Control control)
    {
        RECT bounds = drawItem.rcItem;
        FillControlBackground(drawItem.hDC, bounds, control);
        _ = PInvoke.SetBkMode(drawItem.hDC, BACKGROUND_MODE.TRANSPARENT);

        RECT textBounds = new()
        {
            left = bounds.left + NativeConstants.GroupBoxTextOffset,
            top = bounds.top,
            right = bounds.right - NativeConstants.GroupBoxTextOffset,
            bottom = bounds.bottom,
        };

        RECT measured = textBounds;
        DrawText(
            drawItem.hDC,
            text,
            ref measured,
            DRAW_TEXT_FORMAT.DT_SINGLELINE | (DRAW_TEXT_FORMAT)NativeConstants.DT_CALCRECT | DRAW_TEXT_FORMAT.DT_NOPREFIX);

        DrawThemedGroupBoxFrame(drawItem, bounds, measured);

        HBRUSH brush = PInvoke.CreateSolidBrush(Control.ToColorRef(control.EffectiveBackColor));
        measured.left -= 1;
        measured.right += 2;
        _ = PInvoke.FillRect(drawItem.hDC, &measured, brush);
        _ = PInvoke.DeleteObject(new HGDIOBJ(brush.Value));

        COLORREF previousTextColor = SetDrawItemTextColor(drawItem, control);
        try
        {
            DrawText(
                drawItem.hDC,
                text,
                ref textBounds,
                DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }
        finally
        {
            _ = PInvoke.SetTextColor(drawItem.hDC, previousTextColor);
        }
    }

    private static void DrawText(HDC deviceContext, string text, ref RECT bounds, DRAW_TEXT_FORMAT format)
    {
        fixed (char* textPtr = text)
        {
            _ = PInvoke.DrawText(deviceContext, new PCWSTR(textPtr), text.Length, ref bounds, format);
        }
    }

    private static HTHEME OpenThemeData(string classList)
    {
        fixed (char* classListPtr = classList)
        {
            return PInvoke.OpenThemeData(default, new PCWSTR(classListPtr));
        }
    }

    private static COLORREF SetDrawItemTextColor(DRAWITEMSTRUCT drawItem, Control? control = null)
    {
        Color color = ((int)drawItem.itemState & NativeConstants.ODS_DISABLED) != 0
            ? SystemColors.GrayText
            : control?.EffectiveForeColor ?? SystemColors.ControlText;

        return PInvoke.SetTextColor(drawItem.hDC, Control.ToColorRef(color));
    }

    private static bool DrawThemedCheckGlyph(DRAWITEMSTRUCT drawItem, RECT glyph, bool isChecked, bool isRadioButton)
    {
        HTHEME theme = ButtonTheme;
        if (theme == default)
        {
            return false;
        }

        int part = isRadioButton ? NativeConstants.BP_RADIOBUTTON : NativeConstants.BP_CHECKBOX;
        int state = GetThemedCheckState(drawItem, isChecked, isRadioButton);
        return PInvoke.DrawThemeBackground(theme, drawItem.hDC, part, state, &glyph, null).Succeeded;
    }

    private static bool DrawThemedButton(DRAWITEMSTRUCT drawItem, RECT bounds, HWND handle)
    {
        HTHEME theme = ButtonTheme;
        if (theme == default)
        {
            return false;
        }

        int state = GetThemedButtonState(drawItem, handle);
        return PInvoke.DrawThemeBackground(
            theme,
            drawItem.hDC,
            NativeConstants.BP_PUSHBUTTON,
            state,
            &bounds,
            null).Succeeded;
    }

    private static int GetThemedButtonState(DRAWITEMSTRUCT drawItem, HWND handle)
    {
        int itemState = (int)drawItem.itemState;
        if ((itemState & NativeConstants.ODS_DISABLED) != 0)
        {
            return NativeConstants.PBS_DISABLED;
        }

        if ((itemState & NativeConstants.ODS_SELECTED) != 0)
        {
            return NativeConstants.PBS_PRESSED;
        }

        if ((itemState & NativeConstants.ODS_FOCUS) != 0 || PInvoke.GetFocus() == handle)
        {
            return NativeConstants.PBS_DEFAULTED;
        }

        return NativeConstants.PBS_NORMAL;
    }

    private static void DrawClassicButton(DRAWITEMSTRUCT drawItem, RECT bounds)
    {
        uint state = NativeConstants.DFCS_BUTTONPUSH;
        int itemState = (int)drawItem.itemState;
        if ((itemState & NativeConstants.ODS_SELECTED) != 0)
        {
            state |= NativeConstants.DFCS_PUSHED;
        }

        if ((itemState & NativeConstants.ODS_DISABLED) != 0)
        {
            state |= NativeConstants.DFCS_INACTIVE;
        }

        _ = PInvoke.DrawFrameControl(drawItem.hDC, ref bounds, NativeConstants.DFC_BUTTON, state);
    }

    private static int GetThemedCheckState(DRAWITEMSTRUCT drawItem, bool isChecked, bool isRadioButton)
    {
        bool pressed = ((int)drawItem.itemState & NativeConstants.ODS_SELECTED) != 0;
        bool disabled = ((int)drawItem.itemState & NativeConstants.ODS_DISABLED) != 0;

        if (isRadioButton)
        {
            if (isChecked)
            {
                return disabled ? NativeConstants.RBS_CHECKEDDISABLED : pressed ? NativeConstants.RBS_CHECKEDPRESSED : NativeConstants.RBS_CHECKEDNORMAL;
            }

            return disabled ? NativeConstants.RBS_UNCHECKEDDISABLED : pressed ? NativeConstants.RBS_UNCHECKEDPRESSED : NativeConstants.RBS_UNCHECKEDNORMAL;
        }

        if (isChecked)
        {
            return disabled ? NativeConstants.CBS_CHECKEDDISABLED : pressed ? NativeConstants.CBS_CHECKEDPRESSED : NativeConstants.CBS_CHECKEDNORMAL;
        }

        return disabled ? NativeConstants.CBS_UNCHECKEDDISABLED : pressed ? NativeConstants.CBS_UNCHECKEDPRESSED : NativeConstants.CBS_UNCHECKEDNORMAL;
    }

    private static void DrawClassicCheckGlyph(DRAWITEMSTRUCT drawItem, RECT glyph, bool isChecked, bool isRadioButton)
    {
        uint state = isRadioButton ? NativeConstants.DFCS_BUTTONRADIO : NativeConstants.DFCS_BUTTONCHECK;
        if (isChecked)
        {
            state |= NativeConstants.DFCS_CHECKED;
        }

        if (((int)drawItem.itemState & NativeConstants.ODS_SELECTED) != 0)
        {
            state |= NativeConstants.DFCS_PUSHED;
        }

        if (((int)drawItem.itemState & NativeConstants.ODS_DISABLED) != 0)
        {
            state |= NativeConstants.DFCS_INACTIVE;
        }

        _ = PInvoke.DrawFrameControl(drawItem.hDC, ref glyph, NativeConstants.DFC_BUTTON, state);
    }

    private static void DrawThemedGroupBoxFrame(DRAWITEMSTRUCT drawItem, RECT bounds, RECT textBounds)
    {
        int textHeight = textBounds.bottom - textBounds.top;
        RECT boxBounds = bounds;
        boxBounds.top += textHeight / 2;

        HTHEME theme = ButtonTheme;
        if (theme == default)
        {
            DrawClassicGroupBoxFrame(drawItem.hDC, boxBounds);
            return;
        }

        int state = ((int)drawItem.itemState & NativeConstants.ODS_DISABLED) != 0
            ? NativeConstants.GBS_DISABLED
            : NativeConstants.GBS_NORMAL;

        RECT clipLeft = boxBounds;
        RECT clipMiddle = boxBounds;
        RECT clipRight = boxBounds;

        clipLeft.right = clipLeft.left + NativeConstants.GroupBoxHeaderWidth;
        clipMiddle.left = clipLeft.right;
        clipMiddle.right = Math.Max(clipMiddle.left, textBounds.right - 3);
        clipMiddle.top = textBounds.bottom;
        clipRight.left = clipMiddle.right;

        _ = PInvoke.DrawThemeBackground(theme, drawItem.hDC, NativeConstants.BP_GROUPBOX, state, &boxBounds, &clipLeft);
        _ = PInvoke.DrawThemeBackground(theme, drawItem.hDC, NativeConstants.BP_GROUPBOX, state, &boxBounds, &clipMiddle);
        _ = PInvoke.DrawThemeBackground(theme, drawItem.hDC, NativeConstants.BP_GROUPBOX, state, &boxBounds, &clipRight);
    }

    private static void DrawClassicGroupBoxFrame(HDC deviceContext, RECT frame)
    {
        HBRUSH brush = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)NativeConstants.COLOR_BTNFACE);
        _ = PInvoke.FrameRect(deviceContext, &frame, brush);
    }
}