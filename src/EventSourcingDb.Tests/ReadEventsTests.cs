using System.Collections.Generic;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class ReadEventsTests : IAsyncLifetime
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
    public async Task ReadsNoEventsIfTheDatabaseIsEmpty()
    {
        var client = _container!.GetClient();
        var didReadEvents = false;

        await foreach (var eventResult in client.ReadEventsAsync("/", new ReadEventsOptions(Recursive: true)))
        {
            Assert.False(eventResult.IsErrorSet);
            didReadEvents = true;
        }

        Assert.False(didReadEvents);
    }

    [Fact]
    public async Task ReadsAllEventsFromTheGivenSubject()
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

        var foundEvents = new List<Event>();

        await foreach (var eventResult in client.ReadEventsAsync("/test", new ReadEventsOptions(Recursive: false)))
        {
            Assert.False(eventResult.IsErrorSet);
            foundEvents.Add(eventResult.Event);
        }

        Assert.Equal(2, foundEvents.Count);
    }

    [Fact]
    public async Task ReadsRecursively()
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

        var foundEvents = new List<Event>();

        await foreach (var eventResult in client.ReadEventsAsync("/", new ReadEventsOptions(Recursive: true)))
        {
            Assert.False(eventResult.IsErrorSet);
            foundEvents.Add(eventResult.Event);
        }

        Assert.Equal(2, foundEvents.Count);
    }

    [Fact]
    public async Task ReadsChronologically()
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

        var foundEvents = new List<Event>();
        var options = new ReadEventsOptions(Recursive: false, Order: Order.Chronological);

        await foreach (var eventResult in client.ReadEventsAsync("/test", options))
        {
            Assert.False(eventResult.IsErrorSet);
            foundEvents.Add(eventResult.Event);
        }

        Assert.Collection(foundEvents,
            foundEvent =>
            {
                Assert.Equal("0", foundEvent.Id);
                Assert.Equal(firstData, foundEvent.GetData<EventData>());
            },
            foundEvent =>
            {
                Assert.Equal("1", foundEvent.Id);
                Assert.Equal(secondData, foundEvent.GetData<EventData>());
            }
        );
    }

    [Fact]
    public async Task ReadsAntiChronologically()
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

        var foundEvents = new List<Event>();
        var options = new ReadEventsOptions(Recursive: false, Order: Order.Antichronological);

        await foreach (var eventResult in client.ReadEventsAsync("/test", options))
        {
            Assert.False(eventResult.IsErrorSet);
            foundEvents.Add(eventResult.Event);
        }

        Assert.Collection(foundEvents,
            foundEvent =>
            {
                Assert.Equal("1", foundEvent.Id);
                Assert.Equal(secondData, foundEvent.GetData<EventData>());
            },
            foundEvent =>
            {
                Assert.Equal("0", foundEvent.Id);
                Assert.Equal(firstData, foundEvent.GetData<EventData>());
            }
        );
    }

    [Fact]
    public async Task ReadsWithLowerBound()
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

        var foundEvents = new List<Event>();
        var options = new ReadEventsOptions(Recursive: false, LowerBound: new Bound("1", BoundType.Inclusive));

        await foreach (var eventResult in client.ReadEventsAsync("/test", options))
        {
            Assert.False(eventResult.IsErrorSet);
            foundEvents.Add(eventResult.Event);
        }

        Assert.Single(foundEvents);
        Assert.Collection(foundEvents,
            foundEvent =>
            {
                Assert.Equal("1", foundEvent.Id);
                Assert.Equal(secondData, foundEvent.GetData<EventData>());
            }
        );
    }

    [Fact]
    public async Task ReadsWithUpperBound()
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

        var foundEvents = new List<Event>();
        var options = new ReadEventsOptions(Recursive: false, UpperBound: new Bound("0", BoundType.Inclusive));

        await foreach (var eventResult in client.ReadEventsAsync("/test", options))
        {
            Assert.False(eventResult.IsErrorSet);
            foundEvents.Add(eventResult.Event);
        }

        Assert.Single(foundEvents);
        Assert.Collection(foundEvents,
            foundEvent =>
            {
                Assert.Equal("0", foundEvent.Id);
                Assert.Equal(firstData, foundEvent.GetData<EventData>());
            }
        );
    }

    [Fact]
    public async Task ReadsFromLatestEvent()
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

        var foundEvents = new List<Event>();
        var options = new ReadEventsOptions(
            Recursive: false,
            FromLatestEvent: new ReadFromLatestEvent(
                "/test",
                "io.eventsourcingdb.test",
                ReadIfEventIsMissing.ReadEverything
            )
        );

        await foreach (var eventResult in client.ReadEventsAsync("/test", options))
        {
            Assert.False(eventResult.IsErrorSet);
            foundEvents.Add(eventResult.Event);
        }

        Assert.Single(foundEvents);
        Assert.Collection(foundEvents,
            foundEvent =>
            {
                Assert.Equal("1", foundEvent.Id);
                Assert.Equal(secondData, foundEvent.GetData<EventData>());
            }
        );
    }

    private record struct EventData(int Value);
}
