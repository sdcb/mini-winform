using System.Collections;

namespace Sdcb.MiniWinForm;

public sealed class ToolStripItemCollection : IEnumerable<ToolStripItem>
{
    private readonly IToolStripItemOwner? _owner;
    private readonly ToolStripMenuItem? _ownerItem;
    private readonly List<ToolStripItem> _items = [];

    internal ToolStripItemCollection(MenuStrip owner)
    {
        _owner = owner;
    }

    internal ToolStripItemCollection(ContextMenuStrip owner)
    {
        _owner = owner;
    }

    internal ToolStripItemCollection(ToolStripMenuItem owner)
    {
        _ownerItem = owner;
    }

    internal ToolStripItemCollection(StatusStrip owner)
    {
        _owner = owner;
    }

    public int Count => _items.Count;

    public ToolStripItem this[int index] => _items[index];

    public ToolStripItem Add(string text)
    {
        ToolStripItem item = RootOwner?.CreateDefaultItem(text) ?? new ToolStripMenuItem(text);
        Add(item);
        return item;
    }

    public int Add(ToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.ParentItem is not null || item.Owner is not null)
        {
            throw new InvalidOperationException("The menu item already belongs to a menu.");
        }

        if (RootOwner is not null && !RootOwner.CanOwnItem(item))
        {
            throw new InvalidOperationException("The item type is not supported by this owner.");
        }

        _items.Add(item);
        item.ParentItem = _ownerItem;
        item.SetOwner(RootOwner);
        NotifyChanged();
        return _items.Count - 1;
    }

    public void AddRange(params ToolStripItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        foreach (ToolStripItem item in items)
        {
            Add(item);
        }
    }

    public IEnumerator<ToolStripItem> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal IEnumerable<ToolStripMenuItem> MenuItems => _items.Cast<ToolStripMenuItem>();

    internal IEnumerable<ToolStripStatusLabel> StatusLabels => _items.Cast<ToolStripStatusLabel>();

    internal IToolStripItemOwner? RootOwner => _owner ?? _ownerItem?.Owner;

    internal void NotifyChanged()
    {
        RootOwner?.NotifyItemsChanged();
    }
}