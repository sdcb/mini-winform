namespace Sdcb.MiniWinForm;

public sealed class ItemCheckEventArgs : EventArgs
{
    public ItemCheckEventArgs(int index, bool newValue, bool currentValue)
    {
        Index = index;
        NewValue = newValue;
        CurrentValue = currentValue;
    }

    public int Index { get; }

    public bool NewValue { get; set; }

    public bool CurrentValue { get; }
}