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

    public async Task<ApiVersion> DetectAsync(CancellationToken token)
    {
        Task<ApiVersion> detection;

        lock (_lock)
        {
            // The probe serves every request that waits for it, so it does not run with the token
            // of the one that started it, and a caller who cancels only stops waiting for it.
            // Every request is bounded by the stall timeout, so the probe cannot run forever.
            detection = _detection ??= Task.Run(probe, CancellationToken.None);
        }

        try
        {
            return await detection.WaitAsync(token).ConfigureAwait(false);
        }
        catch when (detection.IsFaulted || detection.IsCanceled)
        {
            lock (_lock)
            {
                if (ReferenceEquals(_detection, detection))
                {
                    _detection = null;
                }
            }

            throw;
        }
    }
}
