namespace Sdcb.MiniWinForm;

public delegate void SplitterEventHandler(object? sender, SplitterEventArgs e);

public sealed class SplitterEventArgs : EventArgs
{
    public SplitterEventArgs(int x, int y, int splitX, int splitY)
    {
        X = x;
        Y = y;
        SplitX = splitX;
        SplitY = splitY;
    }

    public int X { get; }

    public int Y { get; }

    public int SplitX { get; }

    public int SplitY { get; }
}