using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// Ends one attempt to start a container once its startup timeout has run out, or once the caller
// cancels. The clock starts when Testcontainers asks Docker to create the container, which is
// after it pulled the image and started Ryuk, so neither counts. While Docker creates the
// container, the attempt ends only once Docker has created it, since Docker might otherwise
// create it only after the attempt looked for it to remove it. It waits for that for the grace
// period at most, and then gives the attempt up.
internal sealed class ContainerStartAttempt : IDisposable
{
    // How long an attempt waits at most for Docker to finish creating a container once the caller
    // cancelled, or once the startup timeout ran out, before it stops waiting and leaves removing
    // the container to the background.
    public static readonly TimeSpan CreateGracePeriod = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _gracePeriod;
    private readonly CancellationTokenSource _endSource = new();
    private readonly CancellationTokenSource _clockSource = new();
    private readonly CancellationTokenSource _graceSource = new();
    private readonly TaskCompletionSource _givenUp = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _clockRegistration;
    private readonly CancellationTokenRegistration _graceRegistration;
    private readonly CancellationTokenRegistration _callerRegistration;
    private bool _isCreating;
    private bool _isEnding;
    private volatile bool _hasAskedToCreate;
    private volatile bool _hasTimedOut;

    public ContainerStartAttempt(TimeSpan startupTimeout, CancellationToken token)
        : this(startupTimeout, CreateGracePeriod, token)
    {
    }

    internal ContainerStartAttempt(TimeSpan startupTimeout, TimeSpan gracePeriod, CancellationToken token)
    {
        _startupTimeout = startupTimeout;
        _gracePeriod = gracePeriod;
        _clockRegistration = _clockSource.Token.Register(() =>
        {
            _hasTimedOut = true;
            End();
        });
        _graceRegistration = _graceSource.Token.Register(GiveUp);
        _callerRegistration = token.Register(End);
    }

    // Ends everything that Testcontainers does for the attempt.
    public CancellationToken Token => _endSource.Token;

    // Completes once the attempt stopped waiting for Docker to create the container, since the
    // grace period ran out.
    public Task GivenUp => _givenUp.Task;

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
            if (_isEnding)
            {
                return;
            }

            _isEnding = true;

            if (_isCreating)
            {
                _graceSource.CancelAfter(_gracePeriod);
                return;
            }
        }

        _endSource.Cancel();
    }

    private void GiveUp()
    {
        lock (_lock)
        {
            if (!_isCreating)
            {
                return;
            }
        }

        // The attempt has given up before its start ends, so that it tells the two apart.
        _givenUp.TrySetResult();
        _endSource.Cancel();
    }

    public void Dispose()
    {
        _callerRegistration.Dispose();
        _clockRegistration.Dispose();
        _graceRegistration.Dispose();
        _clockSource.Dispose();
        _graceSource.Dispose();
        _endSource.Dispose();
    }
}
