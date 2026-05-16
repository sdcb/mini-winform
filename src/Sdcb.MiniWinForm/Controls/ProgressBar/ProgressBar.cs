using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sdcb.MiniWinForm;

public sealed class ProgressBar : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private int _step = 10;
    private ProgressBarStyle _style = ProgressBarStyle.Blocks;
    private int _marqueeAnimationSpeed = 100;

    public ProgressBar()
        : base(tabStop: false, width: 100, height: 23)
    {
    }

    public int Minimum
    {
        get => _minimum;
        set
        {
            Application.VerifyUiThread();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_minimum == value)
            {
                return;
            }

            if (_maximum < value)
            {
                _maximum = value;
            }

            _minimum = value;
            if (_value < _minimum)
            {
                _value = _minimum;
            }

            UpdateRange();
            UpdatePosition();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            Application.VerifyUiThread();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_maximum == value)
            {
                return;
            }

            if (_minimum > value)
            {
                _minimum = value;
            }

            _maximum = value;
            if (_value > _maximum)
            {
                _value = _maximum;
            }

            UpdateRange();
            UpdatePosition();
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            Application.VerifyUiThread();
            if (value < _minimum || value > _maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be between Minimum and Maximum.");
            }

            if (_value == value)
            {
                return;
            }

            _value = value;
            UpdatePosition();
        }
    }

    public int Step
    {
        get => _step;
        set
        {
            Application.VerifyUiThread();
            _step = value;
            if (IsHandleCreated)
            {
                _ = PInvoke.SendMessage(NativeHandle, NativeConstants.PBM_SETSTEP, new WPARAM((nuint)_step), new LPARAM(0));
            }
        }
    }

    public ProgressBarStyle Style
    {
        get => _style;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_style == value)
            {
                return;
            }

            _style = value;
            RecreateHandle();
            if (_style == ProgressBarStyle.Marquee)
            {
                StartMarquee();
            }
        }
    }

    public int MarqueeAnimationSpeed
    {
        get => _marqueeAnimationSpeed;
        set
        {
            Application.VerifyUiThread();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _marqueeAnimationSpeed = value;
            StartMarquee();
        }
    }

    internal override string NativeClassName => "msctls_progress32";

    internal override WINDOW_STYLE NativeStyle
    {
        get
        {
            WINDOW_STYLE style = base.NativeStyle;
            if (Style == ProgressBarStyle.Continuous)
            {
                style |= (WINDOW_STYLE)NativeConstants.PBS_SMOOTH;
            }
            else if (Style == ProgressBarStyle.Marquee)
            {
                style |= (WINDOW_STYLE)NativeConstants.PBS_MARQUEE;
            }

            return style;
        }
    }

    internal override void CreateHandle()
    {
        base.CreateHandle();
        UpdateRange();
        if (IsHandleCreated)
        {
            _ = PInvoke.SendMessage(NativeHandle, NativeConstants.PBM_SETSTEP, new WPARAM((nuint)_step), new LPARAM(0));
        }

        UpdatePosition();
        StartMarquee();
    }

    public void Increment(int value)
    {
        Application.VerifyUiThread();
        ThrowIfMarquee(nameof(Increment));

        _value += value;
        if (_value < _minimum)
        {
            _value = _minimum;
        }

        if (_value > _maximum)
        {
            _value = _maximum;
        }

        UpdatePosition();
    }

    public void PerformStep()
    {
        Application.VerifyUiThread();
        ThrowIfMarquee(nameof(PerformStep));
        Increment(_step);
    }

    public override string ToString() => $"{base.ToString()}, Minimum: {Minimum}, Maximum: {Maximum}, Value: {_value}";

    private void UpdateRange()
    {
        if (IsHandleCreated)
        {
            _ = PInvoke.SendMessage(NativeHandle, NativeConstants.PBM_SETRANGE32, new WPARAM((nuint)_minimum), new LPARAM(_maximum));
        }
    }

    private void UpdatePosition()
    {
        if (IsHandleCreated)
        {
            _ = PInvoke.SendMessage(NativeHandle, NativeConstants.PBM_SETPOS, new WPARAM((nuint)_value), new LPARAM(0));
        }
    }

    private void StartMarquee()
    {
        if (IsHandleCreated && _style == ProgressBarStyle.Marquee)
        {
            WPARAM enabled = new(_marqueeAnimationSpeed == 0 ? 0u : 1u);
            _ = PInvoke.SendMessage(NativeHandle, NativeConstants.PBM_SETMARQUEE, enabled, new LPARAM(_marqueeAnimationSpeed));
        }
    }

    private void ThrowIfMarquee(string methodName)
    {
        if (Style == ProgressBarStyle.Marquee)
        {
            throw new InvalidOperationException($"{methodName} cannot be called when Style is Marquee.");
        }
    }
}