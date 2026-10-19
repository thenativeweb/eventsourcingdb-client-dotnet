using System;
using System.Threading;

namespace EventSourcingDb;

// Ends one attempt to start a container once its startup timeout has run out, or once the caller
// cancels. The clock starts when Testcontainers asks Docker to create the container, which is
// after it pulled the image and started Ryuk, so neither counts. While Docker creates the
// container, the attempt ends only once Docker has created it, since Docker might otherwise
// create it only after the attempt looked for it to remove it.
internal sealed class ContainerStartAttempt : IDisposable
{
    private readonly object _lock = new();
    private readonly TimeSpan _startupTimeout;
    private readonly CancellationTokenSource _endSource = new();
    private readonly CancellationTokenSource _clockSource = new();
    private readonly CancellationTokenRegistration _clockRegistration;
    private readonly CancellationTokenRegistration _callerRegistration;
    private bool _isCreating;
    private bool _isEnding;
    private volatile bool _hasAskedToCreate;
    private volatile bool _hasTimedOut;

    public ContainerStartAttempt(TimeSpan startupTimeout, CancellationToken token)
    {
        _startupTimeout = startupTimeout;
        _clockRegistration = _clockSource.Token.Register(() =>
        {
            _hasTimedOut = true;
            End();
        });
        _callerRegistration = token.Register(End);
    }

    // Ends everything that Testcontainers does for the attempt.
    public CancellationToken Token => _endSource.Token;

    // Whether Docker was asked to create the container, so that a container of the attempt may
    // exist.
    public bool HasAskedToCreate => _hasAskedToCreate;

    public bool HasTimedOut => _hasTimedOut;

    // Testcontainers calls this right before it asks Docker to create the container. Once the
    // attempt is ending, it asks Docker for nothing more.
    public void BeginCreating()
    {
        lock (_lock)
        {
            if (_isEnding)
            {
                throw new OperationCanceledException(Token);
            }

            _isCreating = true;
            _hasAskedToCreate = true;
        }

        _clockSource.CancelAfter(_startupTimeout);
    }

    // Docker has created the container, or failed to.
    public void EndCreating()
    {
        lock (_lock)
        {
            _isCreating = false;

            if (!_isEnding)
            {
                return;
            }
        }

        _endSource.Cancel();
    }

    private void End()
    {
        lock (_lock)
        {
            _isEnding = true;

            if (_isCreating)
            {
                return;
            }
        }

        _endSource.Cancel();
    }

    public void Dispose()
    {
        _callerRegistration.Dispose();
        _clockRegistration.Dispose();
        _clockSource.Dispose();
        _endSource.Dispose();
    }
}
