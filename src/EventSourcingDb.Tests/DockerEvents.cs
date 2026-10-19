using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Clients;

namespace EventSourcingDb.Tests;

// Watches Docker for the containers that carry a label, from creating them until removing them.
// Docker keeps only its latest events, and other tests use the same Docker, so the events are
// watched while a test runs, instead of asked for afterwards.
internal sealed class DockerEvents : IAsyncDisposable, IProgress<Message>
{
    // Docker hands out an event a moment after it happened, so the events count as complete only
    // once none has arrived for this long.
    private static readonly TimeSpan _quietTime = TimeSpan.FromMilliseconds(500);

    // Ends the wait for events that do not arrive, so that a test fails instead of waiting forever.
    private static readonly TimeSpan _guardTimeout = TimeSpan.FromSeconds(10);

    private readonly IDockerClient _dockerClient = ContainerStart.CreateDockerClient();
    private readonly CancellationTokenSource _stopSource = new();
    private readonly ConcurrentQueue<Message> _messages = new();
    private readonly Task _watchTask;
    private long _lastArrival = Environment.TickCount64;

    public DockerEvents(string label)
    {
        var parameters = new ContainerEventsParameters
        {
            // Docker hands out the events since then first, so that none goes missing while the
            // watch begins.
            Since = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            Filters = new FilterByProperty()
                .Add("type", "container")
                .Add("event", "create")
                .Add("event", "destroy")
                .Add("label", label),
        };

        _watchTask = _dockerClient.System.MonitorEventsAsync(parameters, this, _stopSource.Token);
    }

    // The value of the start label of each container that Docker created, in the order in which
    // Docker created them.
    public IReadOnlyList<string> CreatedStartIds => _messages
        .Where(message => message.Action == "create")
        .Select(message => message.Actor.Attributes[ContainerStart.StartLabel])
        .ToList();

    public void Report(Message value)
    {
        _messages.Enqueue(value);
        Volatile.Write(ref _lastArrival, Environment.TickCount64);
    }

    // Waits until Docker created at least the given number of containers, until it removed every
    // container that it created, and until no more events arrive. If that does not happen in
    // time, it returns nonetheless, so that the test tells what went wrong.
    public async Task SettleAsync(int minimumCreatedCount, CancellationToken token)
    {
        var deadline = Environment.TickCount64 + (long)_guardTimeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            var messages = _messages.ToArray();
            var createdCount = messages.Count(message => message.Action == "create");
            var destroyedCount = messages.Count(message => message.Action == "destroy");

            if (createdCount >= minimumCreatedCount
                && destroyedCount == createdCount
                && Environment.TickCount64 - Volatile.Read(ref _lastArrival) >= _quietTime.TotalMilliseconds)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopSource.CancelAsync();

        try
        {
            await _watchTask;
        }
        catch (OperationCanceledException)
        {
            // Ignored, the watch has ended
        }

        _dockerClient.Dispose();
        _stopSource.Dispose();
    }
}
