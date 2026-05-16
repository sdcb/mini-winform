using System.Collections;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class ListBox : Control
{
    private readonly ObjectCollection _items;
    private int _selectedIndex = -1;

    public ListBox()
        : base(tabStop: true, width: 120, height: 96)
    {
        _items = new ObjectCollection(this);
    }

    public ObjectCollection Items => _items;

    public int SelectedIndex
    {
        get
        {
            Application.VerifyUiThread();
            if (IsHandleCreated)
            {
                _selectedIndex = NativeListBox.GetSelectedIndex(NativeHandle);
            }

            return _selectedIndex;
        }
        set
        {
            Application.VerifyUiThread();
            if (value < -1 || value >= Items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "SelectedIndex must be -1 or a valid item index.");
            }

            SetSelectedIndex(value, updateNative: true, raiseEvent: true);
        }
    }

    public object? SelectedItem => SelectedIndex == -1 ? null : Items[SelectedIndex];

    public event EventHandler? SelectedIndexChanged;

    internal override string NativeClassName => "LISTBOX";

    internal override WINDOW_STYLE NativeStyle =>
        base.NativeStyle |
        WINDOW_STYLE.WS_VSCROLL |
        (WINDOW_STYLE)NativeConstants.LBS_NOTIFY |
        (WINDOW_STYLE)NativeConstants.LBS_NOINTEGRALHEIGHT;

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;

    internal override void CreateHandle()
    {
        base.CreateHandle();
        foreach (object item in Items)
        {
            NativeListBox.AddString(NativeHandle, GetItemText(item));
        }

        if (_selectedIndex >= 0 && _selectedIndex < Items.Count)
        {
            NativeListBox.SetSelectedIndex(NativeHandle, _selectedIndex);
        }
    }

    internal override void OnCommand(int notificationCode)
    {
        if (notificationCode == NativeConstants.LBN_SELCHANGE)
        {
            SetSelectedIndex(NativeListBox.GetSelectedIndex(NativeHandle), updateNative: false, raiseEvent: true);
        }
    }

    private void SetSelectedIndex(int value, bool updateNative, bool raiseEvent)
    {
        if (_selectedIndex == value)
        {
            return;
        }

        _selectedIndex = value;
        if (updateNative && IsHandleCreated)
        {
            NativeListBox.SetSelectedIndex(NativeHandle, _selectedIndex);
        }

        if (raiseEvent)
        {
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnItemAdded(int index, object item)
    {
        if (IsHandleCreated)
        {
            NativeListBox.InsertString(NativeHandle, index, GetItemText(item));
        }
    }

    private void OnItemRemoved(int index)
    {
        if (IsHandleCreated)
        {
            NativeListBox.DeleteString(NativeHandle, index);
        }

        if (_selectedIndex == index)
        {
            SetSelectedIndex(-1, updateNative: true, raiseEvent: true);
        }
        else if (_selectedIndex > index)
        {
            SetSelectedIndex(_selectedIndex - 1, updateNative: true, raiseEvent: false);
        }
    }

    private void OnItemsCleared()
    {
        if (IsHandleCreated)
        {
            NativeListBox.ResetContent(NativeHandle);
        }

        SetSelectedIndex(-1, updateNative: false, raiseEvent: true);
    }

    private static string GetItemText(object item) => item.ToString() ?? string.Empty;

    public sealed class ObjectCollection : IList<object>
    {
        private readonly ListBox _owner;
        private readonly List<object> _items = [];

        internal ObjectCollection(ListBox owner)
        {
            _owner = owner;
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public object this[int index]
        {
            get => _items[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _items[index] = value;
                if (_owner.IsHandleCreated)
                {
                    int selectedIndex = _owner.SelectedIndex;
                    NativeListBox.DeleteString(_owner.NativeHandle, index);
                    NativeListBox.InsertString(_owner.NativeHandle, index, GetItemText(value));
                    if (selectedIndex == index)
                    {
                        NativeListBox.SetSelectedIndex(_owner.NativeHandle, selectedIndex);
                    }
                }
            }
        }

        public int Add(object item)
        {
            ArgumentNullException.ThrowIfNull(item);
            int index = _items.Count;
            _items.Add(item);
            _owner.OnItemAdded(index, item);
            return index;
        }

        void ICollection<object>.Add(object item) => Add(item);

        public void AddRange(params object[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (object item in items)
            {
                Add(item);
            }
        }

        public void Clear()
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items.Clear();
            _owner.OnItemsCleared();
        }

        public bool Contains(object item) => _items.Contains(item);

        public void CopyTo(object[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();

        public int IndexOf(object item) => _items.IndexOf(item);

        public void Insert(int index, object item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Insert(index, item);
            _owner.OnItemAdded(index, item);
            if (_owner._selectedIndex >= index)
            {
                _owner.SetSelectedIndex(_owner._selectedIndex + 1, updateNative: true, raiseEvent: false);
            }
        }

        public bool Remove(object item)
        {
            int index = IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
            _owner.OnItemRemoved(index);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

internal static unsafe class NativeListBox
{
    internal static int GetSelectedIndex(HWND handle)
    {
        LRESULT result = PInvoke.SendMessage(handle, NativeConstants.LB_GETCURSEL, new WPARAM(0), new LPARAM(0));
        return result.Value == NativeConstants.LB_ERR ? -1 : (int)result.Value;
    }

    internal static void SetSelectedIndex(HWND handle, int selectedIndex)
    {
        _ = PInvoke.SendMessage(handle, NativeConstants.LB_SETCURSEL, new WPARAM((nuint)selectedIndex), new LPARAM(0));
    }

    internal static int AddString(HWND handle, string text)
    {
        fixed (char* textPtr = text)
        {
            LRESULT result = PInvoke.SendMessage(handle, NativeConstants.LB_ADDSTRING, new WPARAM(0), new LPARAM((nint)textPtr));
            return (int)result.Value;
        }
    }

    internal static int InsertString(HWND handle, int index, string text)
    {
        fixed (char* textPtr = text)
        {
            LRESULT result = PInvoke.SendMessage(handle, NativeConstants.LB_INSERTSTRING, new WPARAM((nuint)index), new LPARAM((nint)textPtr));
            return (int)result.Value;
        }
    }

    internal static void DeleteString(HWND handle, int index)
    {
        _ = PInvoke.SendMessage(handle, NativeConstants.LB_DELETESTRING, new WPARAM((nuint)index), new LPARAM(0));
    }

    internal static void ResetContent(HWND handle)
    {
        _ = PInvoke.SendMessage(handle, NativeConstants.LB_RESETCONTENT, new WPARAM(0), new LPARAM(0));
    }

    internal static void SetItemHeight(HWND handle, int itemHeight)
    {
        _ = PInvoke.SendMessage(handle, NativeConstants.LB_SETITEMHEIGHT, new WPARAM(0), new LPARAM(itemHeight));
    }
}