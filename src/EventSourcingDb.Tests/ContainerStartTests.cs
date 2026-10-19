using System;
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
        var exception = await Assert.ThrowsAsync<ContainerStartFailedException>(() =>
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
        var exception = await Assert.ThrowsAsync<ContainerStartFailedException>(() =>
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

        var exception = await Assert.ThrowsAsync<ContainerStartFailedException>(() =>
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
        var exception = await Assert.ThrowsAsync<ContainerStartFailedException>(() =>
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

        var exception = await Assert.ThrowsAsync<ContainerStartFailedException>(() =>
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

        var exception = await Assert.ThrowsAsync<ContainerStartFailedException>(() =>
            ContainerStart.StartAsync(builder, 3000, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)
        );

        var dockerException = Assert.IsAssignableFrom<DockerApiException>(exception.InnerException);
        Assert.Equal($"Failed to start the container: {dockerException.Message}", exception.Message);

        await _events.SettleAsync(1, TestContext.Current.CancellationToken);
        Assert.Single(_events.CreatedStartIds);
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task FailsAtOnceWithTheErrorOfDockerIfInspectingTheContainerFailsWhileWaiting()
    {
        // The database never becomes reachable, so the start waits until Docker fails to inspect the
        // container that this test removes.
        var starting = ContainerStart.StartAsync(CreateBuilder(httpPort: 3001), 3000, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var containerId = await _events.WaitForStartAsync(TestContext.Current.CancellationToken);
        using (var dockerClient = ContainerStart.CreateDockerClient())
        {
            await dockerClient.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                TestContext.Current.CancellationToken
            );
        }

        var exception = await Assert.ThrowsAsync<ContainerStartFailedException>(() => starting);

        var dockerException = Assert.IsAssignableFrom<DockerApiException>(exception.InnerException);
        Assert.Equal($"Failed to start the container: {dockerException.Message}", exception.Message);

        await _events.SettleAsync(1, TestContext.Current.CancellationToken);
        Assert.Single(_events.CreatedStartIds);
        Assert.Empty(await ListContainersAsync());
    }

    [Fact]
    public async Task RemovesTheContainerIfTheCallerCancelsWhileDockerCreatesIt()
    {
        using var callerSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // The pull policy runs right before Docker creates the container, so the caller cancels
        // while Docker creates it.
        var builder = CreateBuilder()
            .WithImagePullPolicy(image =>
            {
                callerSource.CancelAfter(TimeSpan.FromMilliseconds(1));
                return image is null;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ContainerStart.StartAsync(builder, 3000, TimeSpan.FromSeconds(20), callerSource.Token)
        );

        await _events.SettleAsync(0, TestContext.Current.CancellationToken);
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

    private async Task<ContainerListResponse[]> ListContainersAsync()
    {
        using var dockerClient = ContainerStart.CreateDockerClient();
        return [.. await ContainerStart.ListContainersAsync(dockerClient, Label)];
    }
}
