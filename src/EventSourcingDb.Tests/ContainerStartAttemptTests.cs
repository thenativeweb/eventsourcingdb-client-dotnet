using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class ContainerStartAttemptTests
{
    // Ends a wait for an attempt to end, so that a test fails instead of waiting forever.
    private static readonly TimeSpan _guardTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StartsTheClockOnlyOnceDockerIsAskedToCreateTheContainer()
    {
        using var attempt = new ContainerStartAttempt(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        Assert.False(attempt.HasTimedOut);
        Assert.False(attempt.Token.IsCancellationRequested);

        attempt.BeginCreating();
        attempt.EndCreating();

        await WaitUntilEndedAsync(attempt);
        Assert.True(attempt.HasTimedOut);
    }

    [Fact]
    public async Task EndsOnlyOnceDockerCreatedTheContainerIfTheTimeRunsOutWhileDockerCreatesIt()
    {
        using var attempt = new ContainerStartAttempt(TimeSpan.FromMilliseconds(1), CancellationToken.None);

        attempt.BeginCreating();
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        Assert.True(attempt.HasTimedOut);
        Assert.False(attempt.Token.IsCancellationRequested);

        attempt.EndCreating();

        Assert.True(attempt.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task EndsOnlyOnceDockerCreatedTheContainerIfTheCallerCancelsWhileDockerCreatesIt()
    {
        using var callerSource = new CancellationTokenSource();
        using var attempt = new ContainerStartAttempt(TimeSpan.FromMinutes(1), callerSource.Token);

        attempt.BeginCreating();
        await callerSource.CancelAsync();

        Assert.False(attempt.Token.IsCancellationRequested);

        attempt.EndCreating();

        Assert.True(attempt.Token.IsCancellationRequested);
        Assert.False(attempt.HasTimedOut);
    }

    [Fact]
    public async Task EndsAtOnceIfTheCallerCancelsWhileDockerCreatesNoContainer()
    {
        using var callerSource = new CancellationTokenSource();
        using var attempt = new ContainerStartAttempt(TimeSpan.FromMinutes(1), callerSource.Token);

        await callerSource.CancelAsync();

        Assert.True(attempt.Token.IsCancellationRequested);
        Assert.Throws<OperationCanceledException>(attempt.BeginCreating);
        Assert.False(attempt.HasAskedToCreate);
    }

    private static async Task WaitUntilEndedAsync(ContainerStartAttempt attempt)
    {
        var ended = new TaskCompletionSource();
        using var registration = attempt.Token.Register(ended.SetResult);

        await ended.Task.WaitAsync(_guardTimeout, TestContext.Current.CancellationToken);
    }
}
