namespace Sdcb.MiniWinForm;

public sealed class ToolStripMenuItem : ToolStripItem
{
    public ToolStripMenuItem()
        : this(string.Empty)
    {
    }

    public ToolStripMenuItem(string? text)
        : base(text)
    {
        DropDownItems = new ToolStripItemCollection(this);
    }

    public ToolStripMenuItem(string? text, EventHandler? onClick)
        : this(text)
    {
        if (onClick is not null)
        {
            Click += onClick;
        }
    }

    public event EventHandler? Click;

    public ToolStripItemCollection DropDownItems { get; }

    public bool HasDropDownItems => DropDownItems.Count > 0;

    public void PerformClick()
    {
        if (Enabled)
        {
            Click?.Invoke(this, EventArgs.Empty);
        }
    }

    internal override void SetOwner(IToolStripItemOwner? owner)
    {
        base.SetOwner(owner);

        foreach (ToolStripMenuItem item in DropDownItems.MenuItems)
        {
            item.SetOwner(owner);
        }
    }
}

internal interface IToolStripItemOwner
{
    ToolStripItem CreateDefaultItem(string text);

    bool CanOwnItem(ToolStripItem item);

    void NotifyItemsChanged();
}