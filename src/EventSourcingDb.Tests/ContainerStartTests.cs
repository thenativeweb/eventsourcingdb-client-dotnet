using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class ContainerStartTests : IAsyncDisposable
{
    // Each test labels its containers with a value of its own, so that it finds them, and only
    // them, while other tests, also those of other client SDKs, use the same Docker.
    private const string TestLabel = "io.eventsourcingdb.client-dotnet.container-start-test";

    private const string ExitedPrefix = "Failed to start the container, it exited before the database became reachable: ";

    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly DockerEvents _events;

    private string Label => $"{TestLabel}={_testId}";

    public ContainerStartTests()
    {
        _events = new DockerEvents(Label);
    }

    [Fact]
    public async Task StartsAContainerWhoseDatabaseBecomesReachable()
    {
        var container = await ContainerStart.StartAsync(CreateBuilder(), 3000, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(
                new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(3000)}/api/v1/ping"),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(await ListContainersAsync());
        }
        finally
        {
            await container.DisposeAsync();
        }

        await _events.SettleAsync(1, TestContext.Current.CancellationToken);
        Assert.Single(_events.CreatedStartIds);
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task ReplacesARunningContainerWhoseDatabaseDoesNotBecomeReachableUpTo3ContainersAndRemovesEachOfThem()
    {
        // The database listens on another port than the one that is published.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ContainerStart.StartAsync(CreateBuilder(httpPort: 3001), 3000, TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken)
        );

        Assert.Equal("Failed to start the container, the database did not become reachable within 1.5s in any of 3 containers", exception.Message);
        Assert.Null(exception.InnerException);

        await _events.SettleAsync(3, TestContext.Current.CancellationToken);
        Assert.Equal(3, _events.CreatedStartIds.Distinct().Count());
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task RemovesAContainerWhoseCreationOutlastsTheStartupTimeout()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ContainerStart.StartAsync(CreateBuilder(), 3000, TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken)
        );

        Assert.Equal("Failed to start the container, the database did not become reachable within 0.001s in any of 3 containers", exception.Message);

        // Docker creates the container even though the time ran out while it did, and the start
        // removes it once Docker has created it.
        await _events.SettleAsync(3, TestContext.Current.CancellationToken);
        Assert.Equal(3, _events.CreatedStartIds.Distinct().Count());
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task DoesNotCountPullingTheImageTowardsTheStartupTimeout()
    {
        // Testcontainers asks the pull policy whether to pull the image right before it would pull
        // it, so a pull policy that takes longer than the startup timeout stands in for a slow pull.
        // The database never becomes reachable, so every container gets created only if its time
        // starts after the pull.
        var builder = CreateBuilder(httpPort: 3001)
            .WithImagePullPolicy(image =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(500));
                return image is null;
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ContainerStart.StartAsync(builder, 3000, TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken)
        );

        Assert.Equal("Failed to start the container, the database did not become reachable within 0.25s in any of 3 containers", exception.Message);

        await _events.SettleAsync(3, TestContext.Current.CancellationToken);
        Assert.Equal(3, _events.CreatedStartIds.Distinct().Count());
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task FailsAtOnceWithTheEndOfTheLogIfTheContainerExitsAndRemovesIt()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ContainerStart.StartAsync(CreateBuilder(extraArguments: "--no-such-flag"), 3000, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)
        );

        Assert.StartsWith(ExitedPrefix, exception.Message);
        Assert.Null(exception.InnerException);

        // The database prints its usage, which ends with this flag, and more than 20 lines in all.
        var logLines = exception.Message[ExitedPrefix.Length..].Split('\n');
        Assert.Equal(20, logLines.Length);
        Assert.Contains(logLines, line => line.Contains("--with-ui", StringComparison.Ordinal));

        await _events.SettleAsync(1, TestContext.Current.CancellationToken);
        Assert.Single(_events.CreatedStartIds);
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task FailsAtOnceWithTheErrorOfDockerIfTheImageDoesNotExist()
    {
        var builder = CreateBuilder(image: $"thenativeweb/eventsourcingdb:non-existent-{Guid.NewGuid()}");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ContainerStart.StartAsync(builder, 3000, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)
        );

        Assert.NotNull(exception.InnerException);
        Assert.Equal($"Failed to start the container: {exception.InnerException.Message}", exception.Message);

        await _events.SettleAsync(0, TestContext.Current.CancellationToken);
        Assert.Empty(_events.CreatedStartIds);
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task FailsAtOnceWithTheErrorOfDockerIfDockerFailsAfterCreatingTheContainerAndRemovesIt()
    {
        // Docker cannot copy a file into another file. Testcontainers copies it before it hands out
        // the container, so the start finds the container by its label only.
        var builder = CreateBuilder().WithResourceMapping("content"u8.ToArray(), "/bin/busybox/file");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ContainerStart.StartAsync(builder, 3000, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)
        );

        var dockerException = Assert.IsAssignableFrom<DockerApiException>(exception.InnerException);
        Assert.Equal($"Failed to start the container: {dockerException.Message}", exception.Message);

        await _events.SettleAsync(1, TestContext.Current.CancellationToken);
        Assert.Single(_events.CreatedStartIds);
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task FailsAtOnceWithTheErrorOfDockerIfDockerFailsWhileTheStartWaitsForTheDatabaseAndRemovesTheContainer()
    {
        // Testcontainers calls the startup callback once the container runs, and before the start
        // pings the database, which never becomes reachable here. Docker cannot copy a file into
        // another file. The startup timeout runs from creating the container, so it leaves a busy
        // Docker the time to create and start it, as otherwise the attempt ends before the
        // callback, and another container replaces it.
        var builder = CreateBuilder(httpPort: 3001)
            .WithStartupCallback((container, token) => container.CopyAsync("content"u8.ToArray(), "/bin/busybox/file", ct: token));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ContainerStart.StartAsync(builder, 3000, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)
        );

        var dockerException = Assert.IsAssignableFrom<DockerApiException>(exception.InnerException);
        Assert.Equal($"Failed to start the container: {dockerException.Message}", exception.Message);

        await _events.SettleAsync(1, TestContext.Current.CancellationToken);
        Assert.Single(_events.CreatedStartIds);
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task LetsDockerFinishCreatingTheContainerIfTheCallerCancelsMeanwhileAndRemovesIt()
    {
        // Docker takes a second to create the container, which is less than the grace period.
        await using var proxy = new DockerProxy(TimeSpan.FromSeconds(1), holdCount: 1);
        using var callerSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var builder = CreateBuilderForProxy(proxy);

        var starting = ContainerStart.StartAsync(builder, 3000, TimeSpan.FromSeconds(20), callerSource.Token);
        await proxy.WaitForCreatesAsync(1, TestContext.Current.CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        await callerSource.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);

        Assert.True(stopwatch.Elapsed < ContainerStartAttempt.CreateGracePeriod, $"The start took {stopwatch.Elapsed} to end.");

        // Docker created the container, although the caller cancelled while it did, and the
        // start removed it.
        await _events.SettleAsync(1, TestContext.Current.CancellationToken);
        Assert.Single(_events.CreatedStartIds);
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task WaitsForTheGracePeriodAtMostIfTheCallerCancelsWhileDockerCreatesTheContainerAndRemovesTheLateContainer()
    {
        // Docker takes longer than the grace period to create the container, but finishes before
        // the start looks for the container a final time.
        await using var proxy = new DockerProxy(TimeSpan.FromSeconds(8), holdCount: 1);
        using var callerSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var builder = CreateBuilderForProxy(proxy);

        var starting = ContainerStart.StartAsync(builder, 3000, TimeSpan.FromSeconds(20), callerSource.Token);
        await proxy.WaitForCreatesAsync(1, TestContext.Current.CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        await callerSource.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);

        AssertGracePeriod(stopwatch.Elapsed);

        await _events.SettleAsync(1, TestContext.Current.CancellationToken);
        Assert.Single(_events.CreatedStartIds);
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task WaitsForTheGracePeriodAtMostIfTheTimeRunsOutWhileDockerCreatesTheContainerAndRemovesTheLateContainer()
    {
        // Docker takes longer than the startup timeout and the grace period to create the first
        // container, but finishes before the start looks for the container a final time. The
        // database never becomes reachable.
        await using var proxy = new DockerProxy(TimeSpan.FromSeconds(8), holdCount: 1);
        var builder = CreateBuilderForProxy(proxy, httpPort: 3001);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ContainerStart.StartAsync(builder, 3000, TimeSpan.FromSeconds(0.5), TestContext.Current.CancellationToken)
        );

        Assert.Equal("Failed to start the container, the database did not become reachable within 0.5s in any of 3 containers", exception.Message);

        // The second container replaces the first one once the startup timeout and the grace period
        // ran out.
        var createArrivals = proxy.CreateArrivals;
        Assert.Equal(3, createArrivals.Count);
        AssertGracePeriod(createArrivals[1] - createArrivals[0] - TimeSpan.FromSeconds(0.5));

        await _events.SettleAsync(3, TestContext.Current.CancellationToken);
        Assert.Equal(3, _events.CreatedStartIds.Distinct().Count());
        Assert.Empty(await ListContainersAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await _events.DisposeAsync();
        await ContainerStart.RemoveContainersAsync(Label);
    }

    private ContainerBuilder CreateBuilder(string? image = null, ushort httpPort = 3000, params string[] extraArguments)
    {
        string[] command = [
            "run",
            "--api-token", "secret",
            "--data-directory-temporary",
            "--http-enabled",
            "--https-enabled=false",
            "--http-port", httpPort.ToString(CultureInfo.InvariantCulture),
            .. extraArguments
        ];

        return new ContainerBuilder(image ?? $"thenativeweb/eventsourcingdb:{DockerfileHelper.GetImageTag()}")
            .WithCommand(command)
            .WithLabel(TestLabel, _testId)
            .WithCleanUp(true);
    }

    // Talks to Docker through the proxy. Without Ryuk, Testcontainers does not create its container
    // through the proxy, which would hold it, too.
    private ContainerBuilder CreateBuilderForProxy(DockerProxy proxy, ushort httpPort = 3000)
    {
        return CreateBuilder(httpPort: httpPort)
            .WithDockerEndpoint(proxy.Endpoint)
            .WithCleanUp(false);
    }

    // The start waits for Docker to create the container for the grace period, and not longer.
    private static void AssertGracePeriod(TimeSpan waited)
    {
        Assert.True(
            waited >= ContainerStartAttempt.CreateGracePeriod - TimeSpan.FromSeconds(0.5)
                && waited < ContainerStartAttempt.CreateGracePeriod + TimeSpan.FromSeconds(2),
            $"The start waited {waited} for Docker to create the container."
        );
    }

    private async Task<ContainerListResponse[]> ListContainersAsync()
    {
        using var dockerClient = ContainerStart.CreateDockerClient();
        return [.. await ContainerStart.ListContainersAsync(dockerClient, Label)];
    }
}
