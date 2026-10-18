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

        var detector = new ApiVersionDetector(() =>
        {
            if (Interlocked.Increment(ref probeCount) > 1)
            {
                return Task.FromResult(ApiVersion.V1);
            }

            probeArrived.SetResult();
            return firstProbe.Task;
        });

        // The only request that waits for the probe stops waiting before the probe fails.
        using var source = new CancellationTokenSource();
        var waiting = detector.DetectAsync(source.Token);
        await probeArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        firstProbe.SetException(new HttpRequestException(HttpRequestError.ConnectionError, "The server was down."));

        Assert.True(SpinWait.SpinUntil(() => !detector.HasDetection, TimeSpan.FromSeconds(5)));

        var apiVersion = await detector.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiVersion.V1, apiVersion);
        Assert.Equal(2, Volatile.Read(ref probeCount));
    }

    [Fact]
    public async Task FailsEveryRequestThatWaitsForAFailedProbeWithItsError()
    {
        var firstProbe = new TaskCompletionSource<ApiVersion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new HttpRequestException(HttpRequestError.ConnectionError, "The server was down.");
        var detector = new ApiVersionDetector(() => firstProbe.Task);

        var first = detector.DetectAsync(TestContext.Current.CancellationToken);
        var second = detector.DetectAsync(TestContext.Current.CancellationToken);
        firstProbe.SetException(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => first));
        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => second));
        Assert.False(detector.HasDetection);
    }

    [Fact]
    public async Task RemembersADefinitiveAnswer()
    {
        var probeCount = 0;
        var detector = new ApiVersionDetector(() =>
        {
            Interlocked.Increment(ref probeCount);
            return Task.FromResult(ApiVersion.V2);
        });

        Assert.Equal(ApiVersion.V2, await detector.DetectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ApiVersion.V2, await detector.DetectAsync(TestContext.Current.CancellationToken));

        Assert.True(detector.HasDetection);
        Assert.Equal(1, Volatile.Read(ref probeCount));
    }
}
