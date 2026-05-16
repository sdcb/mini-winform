using System.Collections;

namespace Sdcb.MiniWinForm;

public sealed class ControlCollection : IEnumerable<Control>
{
    private readonly Control _owner;
    private readonly List<Control> _items = [];

    internal ControlCollection(Control owner)
    {
        _owner = owner;
    }

    public int Count => _items.Count;

    public Control this[int index] => _items[index];

    public void Add(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (control.Parent is not null)
        {
            throw new InvalidOperationException("The control already has a parent.");
        }

        _items.Add(control);
        bool oldEnabled = control.Enabled;
        control.Parent = _owner;
        _owner.PerformLayout();

        if (oldEnabled != control.Enabled)
        {
            control.NotifyParentEnabledChanged(EventArgs.Empty);
        }

        if (_owner.IsHandleCreated)
        {
            control.CreateHandle();
        }
    }

    public IEnumerator<Control> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}