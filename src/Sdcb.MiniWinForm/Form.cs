using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class Form : Control
{
    private static readonly NativeWindowClass NativeClass = new("MiniWinForm.Form");
    private readonly Dictionary<int, ToolStripMenuItem> _menuItemsById = [];
    private MenuStrip? _mainMenuStrip;
    private HMENU _nativeMenu;
    private IWin32Window? _owner;
    private bool _modal;
    private bool _shown;
    private FormBorderStyle _formBorderStyle = FormBorderStyle.Sizable;
    private bool _controlBox = true;
    private bool _maximizeBox = true;
    private bool _minimizeBox = true;
    private DialogResult _dialogResult;
    private Icon? _icon;

    public Form()
        : base(tabStop: false, width: 300, height: 300)
    {
    }

    internal override string NativeClassName => NativeClass.Name;

    public DialogResult DialogResult
    {
        get => _dialogResult;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _dialogResult = value;
        }
    }

    public bool Modal => _modal;

    public FormBorderStyle FormBorderStyle
    {
        get => _formBorderStyle;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_formBorderStyle == value)
            {
                return;
            }

            _formBorderStyle = value;
            UpdateFormStyles();
        }
    }

    public bool ControlBox
    {
        get => _controlBox;
        set
        {
            Application.VerifyUiThread();
            if (_controlBox == value)
            {
                return;
            }

            _controlBox = value;
            UpdateFormStyles();
        }
    }

    public bool MaximizeBox
    {
        get => _maximizeBox;
        set
        {
            Application.VerifyUiThread();
            if (_maximizeBox == value)
            {
                return;
            }

            _maximizeBox = value;
            UpdateFormStyles();
        }
    }

    public bool MinimizeBox
    {
        get => _minimizeBox;
        set
        {
            Application.VerifyUiThread();
            if (_minimizeBox == value)
            {
                return;
            }

            _minimizeBox = value;
            UpdateFormStyles();
        }
    }

    public MenuStrip? MainMenuStrip
    {
        get => _mainMenuStrip;
        set
        {
            Application.VerifyUiThread();
            if (_mainMenuStrip == value)
            {
                return;
            }

            _mainMenuStrip?.Detach(this);
            _mainMenuStrip = value;
            _mainMenuStrip?.Attach(this);

            if (IsHandleCreated)
            {
                ApplyMainMenu();
            }
        }
    }

    public Icon? Icon
    {
        get => _icon;
        set
        {
            Application.VerifyUiThread();
            if (_icon == value)
            {
                return;
            }

            _icon = value;
            UpdateWindowIcon(redrawFrame: true);
        }
    }

    internal override void CreateHandle()
    {
        if (IsHandleCreated)
        {
            return;
        }

        NativeClass.Register();
        NativeWindow.CreateTopLevelWindow(this, NativeClass.Name);
        UpdateWindowIcon(redrawFrame: false);
        ApplyMainMenu();
        PerformLayout();

        foreach (Control child in Controls)
        {
            child.CreateHandle();
        }
    }

    private void UpdateFormStyles()
    {
        if (IsHandleCreated)
        {
            NativeWindow.UpdateFormStyles(NativeHandle, this);
        }
    }

    private void UpdateWindowIcon(bool redrawFrame)
    {
        if (IsHandleCreated)
        {
            NativeIcon.SetWindowIcon(NativeWindow.ToIntPtr(NativeHandle), Icon, redrawFrame);
        }
    }

    public void Show()
    {
        Show(owner: null);
    }

    public void Show(IWin32Window? owner)
    {
        if (owner == this)
        {
            throw new ArgumentException("A form cannot own itself.", nameof(owner));
        }

        if (_shown)
        {
            throw new InvalidOperationException("A visible form cannot be shown again.");
        }

        if (!IsHandleCreated)
        {
            ShowCore(owner);
            return;
        }

        ShowCore(owner);
    }

    internal void ShowDialogWindow()
    {
        ShowCore(_owner);
    }

    private void ShowCore(IWin32Window? owner)
    {
        if (!IsHandleCreated)
        {
            CreateHandle();
        }

        _owner = owner;
        NativeWindow.SetOwner(NativeHandle, owner);
        NativeControl.Show(NativeHandle, true);
        _shown = true;
        NativeWindow.Update(NativeHandle);

        Control? firstTabStop = FindFirstTabStopControl();
        if (firstTabStop is not null)
        {
            NativeControl.Focus(firstTabStop);
        }
    }

    public DialogResult ShowDialog() => ShowDialog(owner: null);

    public DialogResult ShowDialog(IWin32Window? owner)
    {
        Application.VerifyUiThread();
        if (owner == this)
        {
            throw new ArgumentException("A form cannot own itself.", nameof(owner));
        }

        if (_shown)
        {
            throw new InvalidOperationException("A visible form cannot be shown as a modal dialog.");
        }

        if (!Enabled)
        {
            throw new InvalidOperationException("A disabled form cannot be shown.");
        }

        if (Modal)
        {
            throw new InvalidOperationException("A modal form cannot be shown as a modal dialog again.");
        }

        bool restoreOwnerEnabled = false;
        if (owner is Control ownerControl && ownerControl.Enabled)
        {
            ownerControl.Enabled = false;
            restoreOwnerEnabled = true;
        }

        IWin32Window? oldOwner = _owner;
        bool oldModal = _modal;
        _owner = owner;
        _modal = true;
        DialogResult = DialogResult.None;

        try
        {
            return Application.RunDialog(this);
        }
        finally
        {
            NativeControl.Show(NativeHandle, false);
            _shown = false;
            if (IsHandleCreated)
            {
                NativeControl.Destroy(NativeHandle);
            }

            _modal = oldModal;
            _owner = oldOwner;
            if (restoreOwnerEnabled && owner is Control ownerControlToRestore)
            {
                ownerControlToRestore.Enabled = true;
                NativeControl.Focus(ownerControlToRestore);
            }
        }
    }

    internal override void OnCommand(int notificationCode)
    {
        _ = notificationCode;
    }

    public void Close()
    {
        Application.VerifyUiThread();
        if (IsHandleCreated)
        {
            NativeWindow.Close(NativeHandle);
        }
    }

    internal void ApplyMainMenu()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        HMENU oldMenu = _nativeMenu;
        _nativeMenu = default;
        _menuItemsById.Clear();

        MenuStrip? menuStrip = GetVisibleMainMenuStrip();
        if (menuStrip is not null && menuStrip.Items.Count > 0)
        {
            _nativeMenu = NativeMenu.Build(menuStrip, _menuItemsById);
        }

        NativeMenu.Set(NativeHandle, _nativeMenu);
        NativeMenu.Destroy(oldMenu);
        PerformLayout();
    }

    internal override void GetClientSize(out int left, out int top, out int width, out int height)
    {
        base.GetClientSize(out left, out top, out width, out height);
        height += GetNativeMainMenuHeight();
    }

    internal override void GetChildWindowOffset(out int left, out int top)
    {
        left = 0;
        top = -GetNativeMainMenuHeight();
    }

    private int GetNativeMainMenuHeight()
    {
        if (!IsHandleCreated || _nativeMenu == default || GetVisibleMainMenuStrip() is null)
        {
            return 0;
        }

        int dpi = NativeApplication.GetCurrentDpi(NativeHandle);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            return PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CYMENU, (uint)dpi);
        }

        return PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYMENU);
    }

    internal LRESULT WndProc(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case NativeConstants.WM_BEGININVOKE:
                InvokeMarshaledCallbacks();
                return new LRESULT(0);
            case NativeConstants.WM_SIZE:
                PerformLayout();
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
            case NativeConstants.WM_COMMAND:
                if (DispatchMenuCommand(wParam, lParam))
                {
                    return new LRESULT(0);
                }

                NativeControl.DispatchCommand(wParam, lParam);
                return new LRESULT(0);
            case NativeConstants.WM_CONTEXTMENU:
                if (wParam.Value != 0)
                {
                    Control? sourceControl = NativeControl.FromHandle(new HWND((nint)wParam.Value));
                    if (sourceControl is not null && sourceControl.ShowContextMenuFromMessage(lParam))
                    {
                        return new LRESULT(0);
                    }
                }

                if (NativeControl.TryGetControlAtScreenPoint(lParam, out Control? hitControl)
                    && hitControl.ShowContextMenuFromMessage(lParam))
                {
                    return new LRESULT(0);
                }

                if (ShowContextMenuFromMessage(lParam))
                {
                    return new LRESULT(0);
                }

                return PInvoke.DefWindowProc(window, message, wParam, lParam);
            case NativeConstants.WM_CLOSE:
                if (Modal)
                {
                    if (DialogResult == DialogResult.None)
                    {
                        DialogResult = DialogResult.Cancel;
                    }

                    NativeControl.Show(NativeHandle, false);
                    _shown = false;
                    return new LRESULT(0);
                }

                return PInvoke.DefWindowProc(window, message, wParam, lParam);
            case NativeConstants.WM_DRAWITEM:
                return NativeControl.DispatchDrawItem(lParam);
            case NativeConstants.WM_CTLCOLOREDIT:
                return ApplyEditControlColor(wParam, lParam);
            case NativeConstants.WM_CTLCOLORBTN:
            case NativeConstants.WM_CTLCOLORSTATIC:
                return ApplyStaticControlColor(wParam, lParam);
            case NativeConstants.WM_DESTROY:
                _shown = false;
                NativeMenu.Destroy(_nativeMenu);
                _nativeMenu = default;
                NativeWindow.Unregister(NativeHandle);
                AssignHandle(default);
                Application.OnFormDestroyed(this);
                return new LRESULT(0);
            default:
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
    }

    private bool DispatchMenuCommand(WPARAM wParam, LPARAM lParam)
    {
        if (lParam.Value != 0)
        {
            return false;
        }

        int commandId = unchecked((ushort)(wParam.Value & 0xffff));
        if (!_menuItemsById.TryGetValue(commandId, out ToolStripMenuItem? item))
        {
            return false;
        }

        item.PerformClick();
        return true;
    }

    private MenuStrip? GetVisibleMainMenuStrip()
    {
        if (_mainMenuStrip is not null && _mainMenuStrip.Parent == this && _mainMenuStrip.Visible)
        {
            return _mainMenuStrip;
        }

        foreach (Control control in Controls)
        {
            if (control is MenuStrip menuStrip && menuStrip.Visible)
            {
                return menuStrip;
            }
        }

        return null;
    }

    internal static LRESULT ApplyStaticControlColor(WPARAM wParam, LPARAM lParam)
    {
        Control? control = NativeControl.FromHandle(new HWND(lParam.Value));
        if (control is TextBox)
        {
            return control.ApplyControlColor(wParam, transparent: false);
        }

        if (control is CheckBox or RadioButton or GroupBox)
        {
            return NativeWindow.ApplyTransparentControlColor(wParam);
        }

        return control is null
            ? NativeWindow.ApplyControlColor(wParam, NativeConstants.COLOR_BTNFACE, transparent: true)
            : control.ApplyControlColor(wParam, transparent: true);
    }

    internal static LRESULT ApplyEditControlColor(WPARAM wParam, LPARAM lParam)
    {
        Control? control = NativeControl.FromHandle(new HWND(lParam.Value));
        return control is null
            ? NativeWindow.ApplyControlColor(wParam, NativeConstants.COLOR_WINDOW, transparent: false)
            : control.ApplyControlColor(wParam, transparent: false);
    }

    internal bool CheckCloseDialog()
    {
        return DialogResult != DialogResult.None || !_shown;
    }

}


internal sealed unsafe class NativeWindowClass
{
    private static readonly WNDPROC SharedWindowProcedure = WindowProcedure;
    private bool _registered;

    internal NativeWindowClass(string name)
    {
        Name = name;
    }

    internal string Name { get; }

    internal void Register(WNDPROC? windowProcedure = null)
    {
        if (_registered)
        {
            return;
        }

        fixed (char* className = Name)
        {
            WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
                lpfnWndProc = windowProcedure ?? SharedWindowProcedure,
                hInstance = Application.Instance != default ? Application.Instance : NativeApplication.GetModuleHandle(),
                hCursor = PInvoke.LoadCursor(default, new PCWSTR((char*)32512)),
                hbrBackground = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)NativeConstants.COLOR_BTNFACE),
                lpszClassName = new PCWSTR(className),
            };

            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed for '{Name}'. LastError={Marshal.GetLastPInvokeError()}.");
            }
        }

        _registered = true;
    }

    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        Form? owner = NativeWindow.FromHandle(window);
        return owner is null
            ? PInvoke.DefWindowProc(window, message, wParam, lParam)
            : owner.WndProc(window, message, wParam, lParam);
    }
}

internal static unsafe class NativeWindow
{
    private static readonly Dictionary<nint, Form> FormsByHandle = [];

    internal static void CreateTopLevelWindow(Form owner, string className)
    {
        fixed (char* classNamePtr = className)
        fixed (char* titlePtr = owner.Text)
        {
            WINDOW_STYLE style = GetWindowStyle(owner);
            if (!owner.Enabled)
            {
                style |= WINDOW_STYLE.WS_DISABLED;
            }

            HWND handle = PInvoke.CreateWindowEx(
                GetWindowExStyle(owner),
                new PCWSTR(classNamePtr),
                new PCWSTR(titlePtr),
                style,
                unchecked((int)0x80000000),
                unchecked((int)0x80000000),
                owner.Width,
                owner.Height,
                default,
                default,
                Application.Instance,
                null);

            if (handle == default)
            {
                throw new InvalidOperationException($"CreateWindowEx failed for '{className}'. LastError={Marshal.GetLastPInvokeError()}.");
            }

            owner.AssignHandle(handle);

            FormsByHandle[(nint)handle.Value] = owner;
        }
    }

    internal static void UpdateFormStyles(HWND handle, Form owner)
    {
        _ = PInvoke.SetWindowLongPtr(
            handle,
            (WINDOW_LONG_PTR_INDEX)NativeConstants.GWL_STYLE,
            (nint)GetWindowStyle(owner));

        _ = PInvoke.SetWindowLongPtr(
            handle,
            (WINDOW_LONG_PTR_INDEX)NativeConstants.GWL_EXSTYLE,
            (nint)GetWindowExStyle(owner));

        const uint flags = NativeConstants.SWP_NOMOVE |
            NativeConstants.SWP_NOSIZE |
            NativeConstants.SWP_NOZORDER |
            NativeConstants.SWP_NOACTIVATE |
            NativeConstants.SWP_FRAMECHANGED;

        if (!PInvoke.SetWindowPos(handle, default, 0, 0, 0, 0, (SET_WINDOW_POS_FLAGS)flags))
        {
            throw new InvalidOperationException($"SetWindowPos failed. LastError={Marshal.GetLastPInvokeError()}.");
        }
    }

    private static WINDOW_STYLE GetWindowStyle(Form owner)
    {
        WINDOW_STYLE style = WINDOW_STYLE.WS_CLIPCHILDREN;

        FillInBorderStyles(owner, ref style);
        FillInBorderIcons(owner, ref style);

        return style;
    }

    private static WINDOW_EX_STYLE GetWindowExStyle(Form owner)
    {
        WINDOW_EX_STYLE exStyle = 0;

        switch (owner.FormBorderStyle)
        {
            case FormBorderStyle.Fixed3D:
                exStyle |= WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;
                break;
            case FormBorderStyle.FixedDialog:
                exStyle |= WINDOW_EX_STYLE.WS_EX_DLGMODALFRAME;
                break;
            case FormBorderStyle.FixedToolWindow:
            case FormBorderStyle.SizableToolWindow:
                exStyle |= WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
                break;
        }

        return exStyle;
    }

    private static void FillInBorderStyles(Form owner, ref WINDOW_STYLE style)
    {
        switch (owner.FormBorderStyle)
        {
            case FormBorderStyle.None:
                style |= WINDOW_STYLE.WS_POPUP;
                break;
            case FormBorderStyle.FixedSingle:
                style |= WINDOW_STYLE.WS_BORDER;
                break;
            case FormBorderStyle.Sizable:
                style |= WINDOW_STYLE.WS_BORDER | WINDOW_STYLE.WS_THICKFRAME;
                break;
            case FormBorderStyle.Fixed3D:
                style |= WINDOW_STYLE.WS_BORDER;
                break;
            case FormBorderStyle.FixedDialog:
                style |= WINDOW_STYLE.WS_BORDER;
                break;
            case FormBorderStyle.FixedToolWindow:
                style |= WINDOW_STYLE.WS_BORDER;
                break;
            case FormBorderStyle.SizableToolWindow:
                style |= WINDOW_STYLE.WS_BORDER | WINDOW_STYLE.WS_THICKFRAME;
                break;
        }
    }

    private static void FillInBorderIcons(Form owner, ref WINDOW_STYLE style)
    {
        if (owner.FormBorderStyle == FormBorderStyle.None)
        {
            return;
        }

        if (!string.IsNullOrEmpty(owner.Text))
        {
            style |= WINDOW_STYLE.WS_CAPTION;
        }

        if (owner.ControlBox)
        {
            style |= WINDOW_STYLE.WS_SYSMENU | WINDOW_STYLE.WS_CAPTION;
        }
        else
        {
            style &= ~WINDOW_STYLE.WS_SYSMENU;
        }

        if (owner.MaximizeBox)
        {
            style |= WINDOW_STYLE.WS_MAXIMIZEBOX;
        }
        else
        {
            style &= ~WINDOW_STYLE.WS_MAXIMIZEBOX;
        }

        if (owner.MinimizeBox)
        {
            style |= WINDOW_STYLE.WS_MINIMIZEBOX;
        }
        else
        {
            style &= ~WINDOW_STYLE.WS_MINIMIZEBOX;
        }
    }

    internal static Form? FromHandle(HWND handle)
    {
        return FormsByHandle.TryGetValue((nint)handle.Value, out Form? form) ? form : null;
    }

    internal static IntPtr ToIntPtr(HWND handle)
    {
        return new IntPtr((nint)handle.Value);
    }

    internal static void Unregister(HWND handle)
    {
        FormsByHandle.Remove((nint)handle.Value);
    }

    internal static void SetOwner(HWND handle, IWin32Window? owner)
    {
        _ = PInvoke.SetWindowLongPtr(
            handle,
            (WINDOW_LONG_PTR_INDEX)NativeConstants.GWLP_HWNDPARENT,
            owner is null ? IntPtr.Zero : owner.Handle);
    }

    internal static void Update(HWND handle)
    {
        PInvoke.UpdateWindow(handle);
    }

    internal static void Close(HWND handle)
    {
        _ = PInvoke.SendMessage(handle, NativeConstants.WM_CLOSE, new WPARAM(0), new LPARAM(0));
    }

    internal static LRESULT ApplyControlColor(WPARAM wParam, int systemColor, bool transparent)
    {
        if (transparent)
        {
            _ = PInvoke.SetBkMode(new HDC((nint)wParam.Value), BACKGROUND_MODE.TRANSPARENT);
        }

        HBRUSH brush = PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)systemColor);
        return new LRESULT((nint)brush.Value);
    }

    internal static LRESULT ApplyTransparentControlColor(WPARAM wParam)
    {
        _ = PInvoke.SetBkMode(new HDC((nint)wParam.Value), BACKGROUND_MODE.TRANSPARENT);
        HGDIOBJ brush = PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.NULL_BRUSH);
        return new LRESULT((nint)brush.Value);
    }
}

internal static unsafe class NativeMenu
{
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_TOPALIGN = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private static int _nextMenuCommandId = NativeConstants.DefaultMenuCommandId;

    internal static HMENU Build(MenuStrip menuStrip, Dictionary<int, ToolStripMenuItem> commands)
    {
        HMENU menu = PInvoke.CreateMenu();
        if (menu == default)
        {
            throw new InvalidOperationException($"CreateMenu failed. LastError={Marshal.GetLastPInvokeError()}.");
        }

        foreach (ToolStripMenuItem item in menuStrip.Items.MenuItems)
        {
            Append(menu, item, commands);
        }

        return menu;
    }

    internal static HMENU BuildPopup(ToolStripItemCollection items, Dictionary<int, ToolStripMenuItem> commands)
    {
        HMENU menu = PInvoke.CreatePopupMenu();
        if (menu == default)
        {
            throw new InvalidOperationException($"CreatePopupMenu failed. LastError={Marshal.GetLastPInvokeError()}.");
        }

        foreach (ToolStripMenuItem item in items.MenuItems)
        {
            Append(menu, item, commands);
        }

        return menu;
    }

    internal static int TrackPopup(HMENU menu, HWND owner, int screenX, int screenY)
    {
        _ = PInvoke.SetForegroundWindow(owner);
        return PInvoke.TrackPopupMenuEx(
            menu,
            TPM_LEFTALIGN | TPM_TOPALIGN | TPM_RETURNCMD,
            screenX,
            screenY,
            owner,
            null).Value;
    }

    internal static void GetWindowRect(HWND window, out int left, out int top, out int right, out int bottom)
    {
        if (!PInvoke.GetWindowRect(window, out RECT rect))
        {
            throw new InvalidOperationException($"GetWindowRect failed. LastError={Marshal.GetLastPInvokeError()}.");
        }

        left = rect.left;
        top = rect.top;
        right = rect.right;
        bottom = rect.bottom;
    }

    internal static void Set(HWND window, HMENU menu)
    {
        if (!PInvoke.SetMenu(window, menu))
        {
            throw new InvalidOperationException($"SetMenu failed. LastError={Marshal.GetLastPInvokeError()}.");
        }

        if (!PInvoke.DrawMenuBar(window))
        {
            throw new InvalidOperationException($"DrawMenuBar failed. LastError={Marshal.GetLastPInvokeError()}.");
        }
    }

    internal static void Destroy(HMENU menu)
    {
        if (menu != default && !PInvoke.DestroyMenu(menu))
        {
            throw new InvalidOperationException($"DestroyMenu failed. LastError={Marshal.GetLastPInvokeError()}.");
        }
    }

    private static void Append(HMENU menu, ToolStripMenuItem item, Dictionary<int, ToolStripMenuItem> commands)
    {
        MENU_ITEM_FLAGS flags = MENU_ITEM_FLAGS.MF_STRING;
        if (!item.Enabled)
        {
            flags |= MENU_ITEM_FLAGS.MF_GRAYED;
        }

        nuint itemValue;
        if (item.HasDropDownItems)
        {
            HMENU popup = PInvoke.CreatePopupMenu();
            if (popup == default)
            {
                throw new InvalidOperationException($"CreatePopupMenu failed. LastError={Marshal.GetLastPInvokeError()}.");
            }

            foreach (ToolStripMenuItem child in item.DropDownItems.MenuItems)
            {
                Append(popup, child, commands);
            }

            flags |= MENU_ITEM_FLAGS.MF_POPUP;
            itemValue = (nuint)popup.Value;
        }
        else
        {
            int commandId = _nextMenuCommandId++;
            commands[commandId] = item;
            itemValue = (nuint)commandId;
        }

        fixed (char* textPtr = item.Text)
        {
            if (!PInvoke.AppendMenu(menu, flags, itemValue, new PCWSTR(textPtr)))
            {
                throw new InvalidOperationException($"AppendMenu failed. LastError={Marshal.GetLastPInvokeError()}.");
            }
        }
    }
}