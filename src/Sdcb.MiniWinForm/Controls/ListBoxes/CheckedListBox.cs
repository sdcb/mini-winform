using System.Collections;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class CheckedListBox : Control
{
    private const int DefaultItemHeight = 18;
    private readonly ObjectCollection _items;
    private readonly CheckedItemCollection _checkedItems;
    private readonly CheckedIndexCollection _checkedIndices;
    private int _selectedIndex = -1;
    private bool _checkOnClick;

    public CheckedListBox()
        : base(tabStop: true, width: 120, height: 96)
    {
        _items = new ObjectCollection(this);
        _checkedItems = new CheckedItemCollection(this);
        _checkedIndices = new CheckedIndexCollection(this);
    }

    public ObjectCollection Items => _items;

    public CheckedItemCollection CheckedItems => _checkedItems;

    public CheckedIndexCollection CheckedIndices => _checkedIndices;

    public bool CheckOnClick
    {
        get => _checkOnClick;
        set
        {
            Application.VerifyUiThread();
            _checkOnClick = value;
        }
    }

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

    public event EventHandler<ItemCheckEventArgs>? ItemCheck;

    internal override string NativeClassName => "LISTBOX";

    internal override WINDOW_STYLE NativeStyle =>
        base.NativeStyle |
        WINDOW_STYLE.WS_VSCROLL |
        (WINDOW_STYLE)NativeConstants.LBS_NOTIFY |
        (WINDOW_STYLE)NativeConstants.LBS_OWNERDRAWFIXED |
        (WINDOW_STYLE)NativeConstants.LBS_HASSTRINGS |
        (WINDOW_STYLE)NativeConstants.LBS_NOINTEGRALHEIGHT;

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;

    public bool GetItemChecked(int index)
    {
        Application.VerifyUiThread();
        ValidateItemIndex(index);
        return Items.GetChecked(index);
    }

    public void SetItemChecked(int index, bool value)
    {
        Application.VerifyUiThread();
        ValidateItemIndex(index);
        SetItemCheckedCore(index, value, raiseEvent: true);
    }

    internal override void CreateHandle()
    {
        base.CreateHandle();
        NativeListBox.SetItemHeight(NativeHandle, DefaultItemHeight);
        foreach (CheckedListBoxItem item in Items.InnerItems)
        {
            NativeListBox.AddString(NativeHandle, GetItemText(item.Value));
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
            int newSelectedIndex = NativeListBox.GetSelectedIndex(NativeHandle);
            SetSelectedIndex(newSelectedIndex, updateNative: false, raiseEvent: true);
            if (CheckOnClick && newSelectedIndex >= 0)
            {
                SetItemChecked(newSelectedIndex, !GetItemChecked(newSelectedIndex));
            }
        }
    }

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        int index = (int)drawItem.itemID;
        if (index < 0 || index >= Items.Count)
        {
            return true;
        }

        CheckedListBoxItem item = Items.InnerItems[index];
        NativeDraw.DrawCheckedListBoxItem(drawItem, GetItemText(item.Value), item.Checked, Enabled);
        return true;
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

    private bool SetItemCheckedCore(int index, bool value, bool raiseEvent)
    {
        CheckedListBoxItem item = Items.InnerItems[index];
        if (item.Checked == value)
        {
            return false;
        }

        bool newValue = value;
        if (raiseEvent)
        {
            ItemCheckEventArgs eventArgs = new(index, value, item.Checked);
            ItemCheck?.Invoke(this, eventArgs);
            newValue = eventArgs.NewValue;
        }

        if (item.Checked == newValue)
        {
            return false;
        }

        item.Checked = newValue;
        InvalidateListBox();
        return true;
    }

    private void OnItemAdded(int index, CheckedListBoxItem item)
    {
        if (IsHandleCreated)
        {
            NativeListBox.InsertString(NativeHandle, index, GetItemText(item.Value));
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

    private void InvalidateListBox()
    {
        if (IsHandleCreated)
        {
            NativeControl.Invalidate(NativeHandle);
        }
    }

    private void ValidateItemIndex(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must refer to an existing item.");
        }
    }

    private static string GetItemText(object item) => item.ToString() ?? string.Empty;

    public sealed class ObjectCollection : IList<object>
    {
        private readonly CheckedListBox _owner;
        private readonly List<CheckedListBoxItem> _items = [];

        internal ObjectCollection(CheckedListBox owner)
        {
            _owner = owner;
        }

        internal List<CheckedListBoxItem> InnerItems => _items;

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public object this[int index]
        {
            get => _items[index].Value;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _items[index].Value = value;
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

        public int Add(object item) => Add(item, isChecked: false);

        public int Add(object item, bool isChecked)
        {
            ArgumentNullException.ThrowIfNull(item);
            int index = _items.Count;
            CheckedListBoxItem listItem = new(item, isChecked);
            _items.Add(listItem);
            _owner.OnItemAdded(index, listItem);
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

        public bool Contains(object item) => IndexOf(item) >= 0;

        public void CopyTo(object[] array, int arrayIndex)
        {
            for (int index = 0; index < _items.Count; index++)
            {
                array[arrayIndex + index] = _items[index].Value;
            }
        }

        public IEnumerator<object> GetEnumerator() => _items.Select(item => item.Value).GetEnumerator();

        public int IndexOf(object item)
        {
            for (int index = 0; index < _items.Count; index++)
            {
                if (Equals(_items[index].Value, item))
                {
                    return index;
                }
            }

            return -1;
        }

        public void Insert(int index, object item)
        {
            ArgumentNullException.ThrowIfNull(item);
            CheckedListBoxItem listItem = new(item, isChecked: false);
            _items.Insert(index, listItem);
            _owner.OnItemAdded(index, listItem);
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

        internal bool GetChecked(int index) => _items[index].Checked;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class CheckedItemCollection : IReadOnlyList<object>
    {
        private readonly CheckedListBox _owner;

        internal CheckedItemCollection(CheckedListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner.Items.InnerItems.Count(item => item.Checked);

        public object this[int index]
        {
            get
            {
                foreach (CheckedListBoxItem item in _owner.Items.InnerItems)
                {
                    if (!item.Checked)
                    {
                        continue;
                    }

                    if (index == 0)
                    {
                        return item.Value;
                    }

                    index--;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public IEnumerator<object> GetEnumerator() => _owner.Items.InnerItems.Where(item => item.Checked).Select(item => item.Value).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class CheckedIndexCollection : IReadOnlyList<int>
    {
        private readonly CheckedListBox _owner;

        internal CheckedIndexCollection(CheckedListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner.Items.InnerItems.Count(item => item.Checked);

        public int this[int index]
        {
            get
            {
                for (int itemIndex = 0; itemIndex < _owner.Items.InnerItems.Count; itemIndex++)
                {
                    if (!_owner.Items.InnerItems[itemIndex].Checked)
                    {
                        continue;
                    }

                    if (index == 0)
                    {
                        return itemIndex;
                    }

                    index--;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (int index = 0; index < _owner.Items.InnerItems.Count; index++)
            {
                if (_owner.Items.InnerItems[index].Checked)
                {
                    yield return index;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class CheckedListBoxItem
    {
        internal CheckedListBoxItem(object value, bool isChecked)
        {
            Value = value;
            Checked = isChecked;
        }

        internal object Value { get; set; }

        internal bool Checked { get; set; }
    }
}