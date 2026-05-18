using System.ComponentModel;

namespace Sdcb.MiniWinForm;

public sealed class WindowsFormsSynchronizationContext : SynchronizationContext
{
    private readonly Control _controlToSendTo;
    private WeakReference<Thread>? _destinationThread;

    internal WindowsFormsSynchronizationContext(Control controlToSendTo)
    {
        if (!controlToSendTo.IsHandleCreated)
        {
            throw new InvalidAsynchronousStateException("Marshaling control must have a created handle.");
        }

        _controlToSendTo = controlToSendTo;
        DestinationThread = Thread.CurrentThread;
    }

    private WindowsFormsSynchronizationContext(Control controlToSendTo, Thread? destinationThread)
    {
        _controlToSendTo = controlToSendTo;
        DestinationThread = destinationThread;
    }

    private Thread? DestinationThread
    {
        get => _destinationThread?.TryGetTarget(out Thread? target) == true ? target : null;
        set
        {
            if (value is null)
            {
                return;
            }

            if (_destinationThread is null)
            {
                _destinationThread = new(value);
                return;
            }

            _destinationThread.SetTarget(value);
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        Thread? destinationThread = DestinationThread;
        if (destinationThread is null || !destinationThread.IsAlive)
        {
            throw new InvalidAsynchronousStateException("The destination thread is no longer valid.");
        }

        _controlToSendTo.Invoke(d, state);
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        _controlToSendTo.BeginInvoke(d, state);
    }

    public override SynchronizationContext CreateCopy()
    {
        return new WindowsFormsSynchronizationContext(_controlToSendTo, DestinationThread);
    }
}