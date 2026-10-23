using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class ApiVersionDetectorTests
{
    [Fact]
    public async Task ForgetsAFailedProbeThatNoRequestWaitsForAnyMore()
    {
        var probeCount = 0;
        var probeArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstProbe = new TaskCompletionSource<ApiVersion>(TaskCreationOptions.RunContinuationsAsynchronously);

        var detector = new ApiVersionDetector();
        Task<ApiVersion> Probe()
        {
            if (Interlocked.Increment(ref probeCount) > 1)
            {
                return Task.FromResult(ApiVersion.V1);
            }

            probeArrived.SetResult();
            return firstProbe.Task;
        }

        // The only request that waits for the probe stops waiting before the probe fails.
        using var source = new CancellationTokenSource();
        var waiting = detector.Detect(Probe).WaitAsync(source.Token);
        await probeArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        firstProbe.SetException(new HttpRequestException(HttpRequestError.ConnectionError, "The server was down."));

        Assert.True(SpinWait.SpinUntil(() => !detector.HasDetection, TimeSpan.FromSeconds(5)));

        var apiVersion = await detector.Detect(Probe).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiVersion.V1, apiVersion);
        Assert.Equal(2, Volatile.Read(ref probeCount));
    }

    [Fact]
    public async Task FailsEveryRequestThatWaitsForAFailedProbeWithItsError()
    {
        var firstProbe = new TaskCompletionSource<ApiVersion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new HttpRequestException(HttpRequestError.ConnectionError, "The server was down.");
        var detector = new ApiVersionDetector();

        var first = detector.Detect(() => firstProbe.Task).WaitAsync(TestContext.Current.CancellationToken);
        var second = detector.Detect(() => throw new InvalidOperationException("A second probe.")).WaitAsync(TestContext.Current.CancellationToken);
        firstProbe.SetException(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => first));
        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => second));
        Assert.False(detector.HasDetection);
    }

    [Fact]
    public async Task RemembersADefinitiveAnswer()
    {
        var probeCount = 0;
        var detector = new ApiVersionDetector();
        Task<ApiVersion> Probe()
        {
            Interlocked.Increment(ref probeCount);
            return Task.FromResult(ApiVersion.V2);
        }

        Assert.Equal(ApiVersion.V2, await detector.Detect(Probe).WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ApiVersion.V2, await detector.Detect(Probe).WaitAsync(TestContext.Current.CancellationToken));

        Assert.True(detector.HasDetection);
        Assert.Equal(1, Volatile.Read(ref probeCount));
    }

    [Fact]
    public async Task ForgetsARememberedV2OnlyWhileItIsTheRememberedAnswer()
    {
        var probeCount = 0;
        var secondProbe = new TaskCompletionSource<ApiVersion>(TaskCreationOptions.RunContinuationsAsynchronously);

        var detector = new ApiVersionDetector();
        Task<ApiVersion> Probe()
        {
            return Interlocked.Increment(ref probeCount) == 1 ? Task.FromResult(ApiVersion.V2) : secondProbe.Task;
        }

        var first = detector.Detect(Probe);
        Assert.Equal(ApiVersion.V2, await first.WaitAsync(TestContext.Current.CancellationToken));

        detector.ForgetV2(first);
        var second = detector.Detect(Probe);
        Assert.NotSame(first, second);

        // The other requests that were sent on the first answer fail meanwhile, which leaves the
        // probe that runs alone, even if it is handed over itself.
        detector.ForgetV2(first);
        detector.ForgetV2(second);
        Assert.Same(second, detector.Detect(Probe));

        // They also fail after the probe found the v2 API again, which keeps its answer.
        secondProbe.SetResult(ApiVersion.V2);
        Assert.Equal(ApiVersion.V2, await second.WaitAsync(TestContext.Current.CancellationToken));
        detector.ForgetV2(first);

        Assert.Same(second, detector.Detect(Probe));
        Assert.Equal(2, Volatile.Read(ref probeCount));
    }

    [Fact]
    public async Task DoesNotForgetARememberedV1()
    {
        var detector = new ApiVersionDetector();

        var detection = detector.Detect(() => Task.FromResult(ApiVersion.V1));
        Assert.Equal(ApiVersion.V1, await detection.WaitAsync(TestContext.Current.CancellationToken));
        detector.ForgetV2(detection);

        Assert.Same(detection, detector.Detect(() => throw new InvalidOperationException("A second probe.")));
    }

    [Fact]
    public async Task KeepsARememberedV1WhileItFindsOutAgain()
    {
        var secondProbe = new TaskCompletionSource<ApiVersion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCount = 0;

        var detector = new ApiVersionDetector();
        Task<ApiVersion> Probe()
        {
            return Interlocked.Increment(ref probeCount) == 1 ? Task.FromResult(ApiVersion.V1) : secondProbe.Task;
        }

        var first = detector.Detect(Probe);
        Assert.Equal(ApiVersion.V1, await first.WaitAsync(TestContext.Current.CancellationToken));

        var second = detector.DetectAgain(first, Probe);
        Assert.NotSame(first, second);

        // Every other request keeps the remembered answer, and the requests that find out again
        // share the probe that runs.
        Assert.Same(first, detector.Detect(Probe, out var isRemembered));
        Assert.True(isRemembered);
        Assert.Same(second, detector.DetectAgain(first, Probe));

        // Once the probe answers, its answer is remembered.
        secondProbe.SetResult(ApiVersion.V2);
        Assert.Equal(ApiVersion.V2, await second.WaitAsync(TestContext.Current.CancellationToken));

        Assert.Same(second, detector.Detect(Probe));
        Assert.Equal(2, Volatile.Read(ref probeCount));
    }

    [Fact]
    public async Task KeepsARememberedV1IfFindingOutAgainFails()
    {
        var probeCount = 0;

        var detector = new ApiVersionDetector();
        Task<ApiVersion> Probe()
        {
            return Interlocked.Increment(ref probeCount) == 1
                ? Task.FromResult(ApiVersion.V1)
                : Task.FromException<ApiVersion>(new InvalidOperationException("The probe failed."));
        }

        var first = detector.Detect(Probe);
        Assert.Equal(ApiVersion.V1, await first.WaitAsync(TestContext.Current.CancellationToken));

        var second = detector.DetectAgain(first, Probe);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.WaitAsync(TestContext.Current.CancellationToken));

        Assert.Same(first, detector.Detect(Probe));

        // The next request that finds out again starts a probe of its own.
        var third = detector.DetectAgain(first, Probe);
        Assert.NotSame(second, third);
        await Assert.ThrowsAsync<InvalidOperationException>(() => third.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, Volatile.Read(ref probeCount));
    }

    // A request that finds out again after another one already did uses that answer, which was
    // found out after the request arrived.
    [Fact]
    public async Task UsesTheAnswerThatWasFoundOutAgainMeanwhile()
    {
        var probeCount = 0;

        var detector = new ApiVersionDetector();
        Task<ApiVersion> Probe()
        {
            Interlocked.Increment(ref probeCount);
            return Task.FromResult(ApiVersion.V1);
        }

        var first = detector.Detect(Probe);
        Assert.Equal(ApiVersion.V1, await first.WaitAsync(TestContext.Current.CancellationToken));

        var second = detector.DetectAgain(first, Probe);
        Assert.Equal(ApiVersion.V1, await second.WaitAsync(TestContext.Current.CancellationToken));

        Assert.Same(second, detector.DetectAgain(first, Probe));
        Assert.Equal(2, Volatile.Read(ref probeCount));
    }

    // Only an answer that was there before is remembered. The answer of a probe that the call
    // starts, or of one that runs, is found out for the call.
    [Fact]
    public async Task TellsWhetherTheAnswerWasRememberedBeforeTheCall()
    {
        var probe = new TaskCompletionSource<ApiVersion>(TaskCreationOptions.RunContinuationsAsynchronously);

        var detector = new ApiVersionDetector();

        var detection = detector.Detect(() => probe.Task, out var isRememberedWhenStarted);
        Assert.False(isRememberedWhenStarted);

        Assert.Same(detection, detector.Detect(() => probe.Task, out var isRememberedWhileRunning));
        Assert.False(isRememberedWhileRunning);

        probe.SetResult(ApiVersion.V1);
        await detection.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Same(detection, detector.Detect(() => probe.Task, out var isRememberedOnceAnswered));
        Assert.True(isRememberedOnceAnswered);
    }
}
