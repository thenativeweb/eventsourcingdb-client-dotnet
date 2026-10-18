using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// Finds out once which API the server offers. Only a definitive answer is remembered: if the
// probe fails, the requests that wait for it fail with its error, and the next request probes
// again. Requests that arrive while the probe runs wait for the same probe.
internal sealed class ApiVersionDetector(Func<Task<ApiVersion>> probe)
{
    private readonly object _lock = new();
    private Task<ApiVersion>? _detection;

    // Whether a probe runs, or its answer is remembered.
    internal bool HasDetection
    {
        get
        {
            lock (_lock)
            {
                return _detection is not null;
            }
        }
    }

    public Task<ApiVersion> DetectAsync(CancellationToken token)
    {
        Task<ApiVersion> detection;

        lock (_lock)
        {
            detection = _detection ??= StartProbe();
        }

        // A caller who cancels only stops waiting for the probe, which goes on for the others.
        return detection.WaitAsync(token);
    }

    // The probe serves every request that waits for it, so it does not run with the token of the
    // one that started it. Every request is bounded by the stall timeout, so the probe cannot run
    // forever. If it fails, it is forgotten before the requests that wait for it learn of the
    // failure, and also if no request waits for it any more.
    private Task<ApiVersion> StartProbe()
    {
        var detection = new TaskCompletionSource<ApiVersion>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            try
            {
                detection.SetResult(await probe().ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_detection, detection.Task))
                    {
                        _detection = null;
                    }
                }

                detection.SetException(exception);

                // The requests that wait for the probe get its error. If none waits any more,
                // the error is not one that went unnoticed.
                _ = detection.Task.Exception;
            }
        }, CancellationToken.None);

        return detection.Task;
    }
}
