using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

// Finds out once which API the server offers. Only a definitive answer is remembered: if the
// probe fails, the requests that wait for it fail with its error, and the next request probes
// again. Requests that arrive while the probe runs wait for the same probe. A remembered v2 is
// forgotten once the server no longer offers it, see ForgetV2, and a remembered v1 is checked
// again before a request is refused for lack of the v2 API, see DetectAgain. A client has one of
// its own, unless the clients of a registration for dependency injection share one.
internal sealed class ApiVersionDetector
{
    private readonly object _lock = new();
    private Task<ApiVersion>? _detection;

    // The probe that checks a remembered v1 again, while it runs. It is kept apart from the
    // remembered answer, which every other request goes on using meanwhile.
    private Task<ApiVersion>? _recheck;

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

    // Returns the probe that runs or whose answer is remembered, and starts the given one if there
    // is neither. A request waits for it, and hands it to ForgetV2 should its answer turn out to be
    // outdated.
    public Task<ApiVersion> Detect(Func<Task<ApiVersion>> probe)
    {
        return Detect(probe, out _);
    }

    // Like Detect, and tells whether the answer was remembered before the call. The answer of a
    // probe that runs, or that the call starts, is found out after the request arrived, so it is
    // not remembered in that sense.
    public Task<ApiVersion> Detect(Func<Task<ApiVersion>> probe, out bool isRemembered)
    {
        lock (_lock)
        {
            isRemembered = _detection is { IsCompleted: true };

            return _detection ??= StartProbe(probe);
        }
    }

    // Forgets that the server offers the v2 API, as the given probe found out, so that the next
    // request probes again. A request calls this once the server answers it through the v2 API
    // with 404 or 405, as after a rollback to a version without it. The requests that were sent on
    // the same answer may fail one after the other, so only that answer is forgotten, and only
    // while it is still the remembered one: a probe that runs meanwhile goes on for the requests
    // that wait for it, and the answer of a newer probe is kept.
    public void ForgetV2(Task<ApiVersion> detection)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_detection, detection) && detection is { IsCompletedSuccessfully: true, Result: ApiVersion.V2 })
            {
                _detection = null;
            }
        }
    }

    // Finds out again whether the server offers the v2 API, which the given remembered answer
    // says it does not. A request that needs the v2 API calls this before it is refused for lack
    // of it, since the server may have been upgraded meanwhile. The requests that call this while
    // the probe runs wait for the same probe, and every other request goes on using the
    // remembered answer, which only a definitive answer of the probe replaces: if the probe fails,
    // v1 stays remembered. If the remembered answer is no longer the given one, it was found out
    // after the request arrived, so the request gets it without another probe.
    public Task<ApiVersion> DetectAgain(Task<ApiVersion> detection, Func<Task<ApiVersion>> probe)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_detection, detection))
            {
                return _detection ??= StartProbe(probe);
            }

            return _recheck ??= StartRecheck(probe);
        }
    }

    // If the probe fails, it is forgotten, so that the next request probes again.
    private Task<ApiVersion> StartProbe(Func<Task<ApiVersion>> probe)
    {
        return RunProbe(probe, (detection, isAnswered) =>
        {
            if (!isAnswered && ReferenceEquals(_detection, detection))
            {
                _detection = null;
            }
        });
    }

    // Once the probe answers, its answer replaces the remembered one. If it fails, the remembered
    // answer stays, and the next request that needs the v2 API checks it again.
    private Task<ApiVersion> StartRecheck(Func<Task<ApiVersion>> probe)
    {
        return RunProbe(probe, (recheck, isAnswered) =>
        {
            _recheck = null;

            if (isAnswered)
            {
                _detection = recheck;
            }
        });
    }

    // The probe serves every request that waits for it, so it does not run with the token of the
    // one that started it. Every request is bounded by the stall timeout, so the probe cannot run
    // forever. Once the probe ends, settle runs under the lock, with whether the probe answered,
    // before the requests that wait for it learn of the outcome, and also if no request waits for
    // it any more. Since settle takes the lock, it runs only after the caller stored the probe.
    private Task<ApiVersion> RunProbe(Func<Task<ApiVersion>> probe, Action<Task<ApiVersion>, bool> settle)
    {
        var detection = new TaskCompletionSource<ApiVersion>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            ApiVersion apiVersion;

            try
            {
                apiVersion = await probe().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lock (_lock)
                {
                    settle(detection.Task, false);
                }

                detection.SetException(exception);

                // The requests that wait for the probe get its error. If none waits any more,
                // the error is not one that went unnoticed.
                _ = detection.Task.Exception;

                return;
            }

            lock (_lock)
            {
                settle(detection.Task, true);
            }

            detection.SetResult(apiVersion);
        }, CancellationToken.None);

        return detection.Task;
    }
}
