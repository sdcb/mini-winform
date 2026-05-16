namespace Sdcb.MiniWinForm;

public sealed class ToolStripStatusLabel : ToolStripItem
{
    private bool _spring;
    private ToolStripStatusLabelBorderSides _borderSides;

    public ToolStripStatusLabel()
    {
    }

    public ToolStripStatusLabel(string? text)
        : base(text)
    {
    }

    public bool Spring
    {
        get => _spring;
        set
        {
            if (_spring == value)
            {
                return;
            }

            _spring = value;
            NotifyChanged();
        }
    }

    public ToolStripStatusLabelBorderSides BorderSides
    {
        get => _borderSides;
        set
        {
            if (_borderSides == value)
            {
                return;
            }

            _borderSides = value;
            NotifyChanged();
        }
    }
}