using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Clients;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace EventSourcingDb;

// Starts a container with EventSourcingDB and waits until the database in it is reachable. A
// container that runs, but whose database does not become reachable within the startup timeout,
// gives way to another one, up to 3 containers in total. If Docker fails, or if the container
// exits, starting fails at once. Whenever an attempt fails, its container is removed at once,
// and Ryuk still removes whatever is left once the tests end.
internal static class ContainerStart
{
    // Every attempt labels its container with an id of its own, by which the attempt finds the
    // container to remove it, even if Testcontainers did not hand it out.
    public const string StartLabel = "io.eventsourcingdb.container-start";

    public const int MaxAttempts = 3;

    private const string CanceledMessage = "The operation was canceled.";

    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan _pingTimeout = TimeSpan.FromSeconds(1);

    public static async Task<IContainer> StartAsync(ContainerBuilder builder, ushort port, TimeSpan startupTimeout, CancellationToken token)
    {
        using var httpClient = new HttpClient { Timeout = _pingTimeout };

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var container = await TryStartAsync(builder, port, startupTimeout, httpClient, token).ConfigureAwait(false);

            if (container is not null)
            {
                return container;
            }
        }

        throw new ContainerStartFailedException(FormattableString.Invariant(
            $"Failed to start the container, the database did not become reachable within {startupTimeout.TotalSeconds}s in any of {MaxAttempts} containers"
        ));
    }

    // Returns null if the database did not become reachable within the startup timeout, so that
    // another container can take the place of this one.
    private static async Task<IContainer?> TryStartAsync(
        ContainerBuilder builder,
        ushort port,
        TimeSpan startupTimeout,
        HttpClient httpClient,
        CancellationToken token)
    {
        var startId = Guid.NewGuid().ToString();
        using var attempt = new ContainerStartAttempt(startupTimeout, token);
        IContainer? container = null;

        try
        {
            container = builder
                .WithLabel(StartLabel, startId)
                .WithExposedPort(port)
                .WithCreateParameterModifier(parameters => PublishPort(parameters, port))
                .WithCreateParameterModifier(_ => attempt.BeginCreating())
                .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(
                    new UntilDatabaseIsReachable(port, httpClient, attempt.Token, token),
                    strategy => strategy
                        .WithMode(WaitStrategyMode.OneShot)
                        .WithInterval(_pollInterval)
                        .WithTimeout(Timeout.InfiniteTimeSpan)
                        .WithRetries(0)))
                .Build();
            container.Created += (_, _) => attempt.EndCreating();

            await container.StartAsync(attempt.Token).ConfigureAwait(false);
            return container;
        }
        catch (Exception exception)
        {
            // Testcontainers reports a container as created only once it has copied the files into
            // it and inspected it, so the attempt stops waiting for that once the start failed.
            attempt.EndCreating();

            // A failure to remove the container never changes the message of the exception.
            var removalException = await RemoveAsync(container, startId, attempt.HasAskedToCreate).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(CanceledMessage, removalException, token);
            }

            // The container exited.
            if (exception is ContainerStartFailedException)
            {
                if (removalException is null)
                {
                    throw;
                }

                throw new ContainerStartFailedException(exception.Message, removalException);
            }

            if (attempt.HasTimedOut && exception is OperationCanceledException or TimeoutException)
            {
                if (removalException is null)
                {
                    return null;
                }

                // Docker failed to remove the container, so it is not replaced.
                throw new ContainerStartFailedException($"Failed to start the container: {removalException.Message}", removalException);
            }

            throw new ContainerStartFailedException(
                $"Failed to start the container: {exception.Message}",
                removalException is null ? exception : new AggregateException(exception, removalException)
            );
        }
    }

    // Testcontainers waits for the ports that it publishes itself for 15 seconds at most, and
    // keeps waiting for them even once the container has exited. So the port is published here,
    // and UntilDatabaseIsReachable waits for it instead. An empty host port lets Docker choose a
    // free one.
    private static void PublishPort(CreateContainerParameters parameters, ushort port)
    {
        var hostConfig = parameters.HostConfig ??= new HostConfig();
        hostConfig.PortBindings ??= new Dictionary<string, IList<PortBinding>>();
        hostConfig.PortBindings[$"{port}/tcp"] = [new PortBinding { HostPort = string.Empty }];
    }

    // Removes the container of an attempt, whether Testcontainers handed it out or not, and
    // returns the first error of Docker if that fails.
    private static async Task<Exception?> RemoveAsync(IContainer? container, string startId, bool mayExist)
    {
        Exception? removalException = null;

        if (container is not null)
        {
            try
            {
                await container.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                removalException = exception;
            }
        }

        if (mayExist)
        {
            try
            {
                await RemoveContainersAsync($"{StartLabel}={startId}").ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                removalException ??= exception;
            }
        }

        return removalException;
    }

    // Lists every container that carries the given label, whether it runs or not.
    internal static async Task<IList<ContainerListResponse>> ListContainersAsync(IDockerClient dockerClient, string label)
    {
        return await dockerClient.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new FilterByProperty().Add("label", label),
        }).ConfigureAwait(false);
    }

    // Removes every container that carries the given label, whether it runs or not.
    internal static async Task RemoveContainersAsync(string label)
    {
        using var dockerClient = CreateDockerClient();

        foreach (var container in await ListContainersAsync(dockerClient, label).ConfigureAwait(false))
        {
            try
            {
                await dockerClient.Containers
                    .RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true, RemoveVolumes = true })
                    .ConfigureAwait(false);
            }
            catch (DockerContainerNotFoundException)
            {
                // The container is gone already.
            }
            catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                // Docker is removing the container already.
            }
        }
    }

    // Talks to the Docker that Testcontainers starts the containers in, as Testcontainers itself
    // does.
    internal static IDockerClient CreateDockerClient()
    {
        return TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder().Build();
    }
}
