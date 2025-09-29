using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class ObserveEventsTests : IAsyncLifetime
{
    private Container? _container;

    public async Task InitializeAsync()
    {
        var imageVersion = DockerfileHelper.GetImageVersionFromDockerfile();
        _container = new Container().WithImageTag(imageVersion);
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
    }

    [Fact]
    public async Task ObserveNoEventsIfTheDatabaseIsEmpty()
    {
        var client = _container!.GetClient();
        var readEvents = new List<Event>();

        var options = new ObserveEventsOptions(Recursive: true);
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            readEvents = await client.ObserveEventsAsync("/", options, source.Token).ToListAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Ignored, we cancel on our own
        }

        Assert.Empty(readEvents);
    }

    [Fact]
    public async Task ObserveAllEventsFromTheGivenSubject()
    {
        var client = _container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent, secondEvent]);

        var observedEvents = new List<Event>();
        var options = new ObserveEventsOptions(Recursive: true);
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            observedEvents = await client.ObserveEventsAsync("/", options, source.Token).ToListAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Ignored, we cancel on our own
        }

        Assert.Equal(2, observedEvents.Count);
    }

    [Fact]
    public async Task ObservesWithLowerBound()
    {
        var client = _container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent, secondEvent]);

        var observedEvents = new List<Event>();
        var options = new ObserveEventsOptions(Recursive: true, LowerBound: new Bound("1", BoundType.Inclusive));
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            observedEvents = await client.ObserveEventsAsync("/", options, source.Token).ToListAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Ignored, we cancel on our own
        }

        Assert.Single(observedEvents);
        Assert.Collection(observedEvents,
            foundEvent =>
            {
                Assert.Equal("1", foundEvent.Id);
                Assert.Equal(secondData, foundEvent.GetData<EventData>());
            }
        );
    }

    [Fact]
    public async Task ObservesFromLatestEvent()
    {
        var client = _container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent, secondEvent]);

        var observedEvents = new List<Event>();
        var options = new ObserveEventsOptions(
            Recursive: true,
            FromLatestEvent: new ObserveFromLatestEvent(
                "/test",
                "io.eventsourcingdb.test",
                ObserveIfEventIsMissing.ReadEverything
            )
        );
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            observedEvents = await client.ObserveEventsAsync("/", options, source.Token).ToListAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Ignored, we cancel on our own
        }

        Assert.Single(observedEvents);
        Assert.Collection(observedEvents,
            foundEvent =>
            {
                Assert.Equal("1", foundEvent.Id);
                Assert.Equal(secondData, foundEvent.GetData<EventData>());
            }
        );
    }

    private record struct EventData(int Value);
}
