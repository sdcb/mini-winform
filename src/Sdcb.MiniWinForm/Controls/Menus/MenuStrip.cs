namespace Sdcb.MiniWinForm;

public sealed class MenuStrip : Control, IToolStripItemOwner
{
    private Form? _owner;

    public MenuStrip()
        : base(tabStop: false, width: 200, height: 24)
    {
        Items = new ToolStripItemCollection(this);
    }

    public ToolStripItemCollection Items { get; }

    internal Form? Owner => _owner ?? Parent as Form;

    internal override string NativeClassName => "STATIC";

    internal override void CreateHandle()
    {
        if (Parent is Form form)
        {
            form.ApplyMainMenu();
        }
    }

    internal void Attach(Form owner)
    {
        if (_owner == owner)
        {
            return;
        }

        if (_owner is not null)
        {
            throw new InvalidOperationException("The menu strip is already assigned to a form.");
        }

        _owner = owner;
        foreach (ToolStripMenuItem item in Items.MenuItems)
        {
            item.SetOwner(this);
        }
    }

    internal void Detach(Form owner)
    {
        if (_owner != owner)
        {
            return;
        }

        _owner = null;
        foreach (ToolStripMenuItem item in Items.MenuItems)
        {
            item.SetOwner(null);
        }
    }

    ToolStripItem IToolStripItemOwner.CreateDefaultItem(string text) => new ToolStripMenuItem(text);

    bool IToolStripItemOwner.CanOwnItem(ToolStripItem item) => item is ToolStripMenuItem;

    void IToolStripItemOwner.NotifyItemsChanged()
    {
        Form? owner = Owner;
        if (owner is not null && owner.IsHandleCreated)
        {
            owner.ApplyMainMenu();
        }
    }
}