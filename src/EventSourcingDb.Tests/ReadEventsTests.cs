using System.Linq;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class ReadEventsTests : EventSourcingDbTests
{
    [Fact]
    public async Task ReadsNoEventsIfTheDatabaseIsEmpty()
    {
        var client = Container!.GetClient();
        var eventsRead = await client.ReadEventsAsync("/", new ReadEventsOptions(Recursive: true)).ToListAsync();

        Assert.Empty(eventsRead);
    }

    [Fact]
    public async Task ReadsAllEventsFromTheGivenSubject()
    {
        var client = Container!.GetClient();

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

        var foundEvents = await client.ReadEventsAsync("/test", new ReadEventsOptions(Recursive: false)).ToListAsync();

        Assert.Equal(2, foundEvents.Count);
    }

    [Fact]
    public async Task ReadsRecursively()
    {
        var client = Container!.GetClient();

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

        var foundEvents = await client.ReadEventsAsync("/", new ReadEventsOptions(Recursive: true)).ToListAsync();

        Assert.Equal(2, foundEvents.Count);
    }

    [Fact]
    public async Task ReadsChronologically()
    {
        var client = Container!.GetClient();

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

        var options = new ReadEventsOptions(Recursive: false, Order: Order.Chronological);

        var foundEvents = await client.ReadEventsAsync("/test", options).ToListAsync();

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
        var client = Container!.GetClient();

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

        var options = new ReadEventsOptions(Recursive: false, Order: Order.Antichronological);

        var foundEvents = await client.ReadEventsAsync("/test", options).ToListAsync();

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
        var client = Container!.GetClient();

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

        var options = new ReadEventsOptions(Recursive: false, LowerBound: new Bound("1", BoundType.Inclusive));

        var foundEvents = await client.ReadEventsAsync("/test", options).ToListAsync();

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
        var client = Container!.GetClient();

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

        var options = new ReadEventsOptions(Recursive: false, UpperBound: new Bound("0", BoundType.Inclusive));

        var foundEvents = await client.ReadEventsAsync("/test", options).ToListAsync();

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
        var client = Container!.GetClient();

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

        var options = new ReadEventsOptions(
            Recursive: false,
            FromLatestEvent: new ReadFromLatestEvent(
                "/test",
                "io.eventsourcingdb.test",
                ReadIfEventIsMissing.ReadEverything
            )
        );

        var foundEvents = await client.ReadEventsAsync("/test", options).ToListAsync();

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
