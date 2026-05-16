namespace Sdcb.MiniWinForm;

public abstract class ToolStripItem
{
    private string _text = string.Empty;
    private bool _enabled = true;
    private bool _autoSize = true;
    private int _width = 100;

    protected ToolStripItem()
    {
    }

    protected ToolStripItem(string? text)
    {
        _text = text ?? string.Empty;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            NotifyChanged();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            NotifyChanged();
        }
    }

    public bool AutoSize
    {
        get => _autoSize;
        set
        {
            if (_autoSize == value)
            {
                return;
            }

            _autoSize = value;
            NotifyChanged();
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            int width = Math.Max(0, value);
            if (_width == width)
            {
                return;
            }

            _width = width;
            NotifyChanged();
        }
    }

    internal IToolStripItemOwner? Owner { get; private set; }

    internal ToolStripMenuItem? ParentItem { get; set; }

    internal virtual void SetOwner(IToolStripItemOwner? owner)
    {
        Owner = owner;
    }

    internal void NotifyChanged()
    {
        Owner?.NotifyItemsChanged();
    }
}