using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class WriteEventsTests : IAsyncLifetime
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
    public async Task WritesASingleEvent()
    {
        var client = _container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEvents([eventCandidate]);

        Assert.Single(writtenEvents);
        Assert.Collection(writtenEvents, writtenEvent => Assert.Equal("0", writtenEvent.Id));
    }

    [Fact]
    public async Task WritesMultipleEvents()
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

        var writtenEvents = await client.WriteEvents([firstEvent, secondEvent]);

        Assert.Equal(2, writtenEvents.Count);
        Assert.Collection(writtenEvents,
            writtenEvent =>
            {
                Assert.Equal("0", writtenEvent.Id);
                Assert.Equal(firstData, writtenEvent.GetData<EventData>());
            },
            writtenEvent =>
            {
                Assert.Equal("1", writtenEvent.Id);
                Assert.Equal(secondData, writtenEvent.GetData<EventData>());
            }
        );
    }

    [Fact]
    public async Task SupportsTheIsSubjectPristinePrecondition()
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

        _ = await client.WriteEvents([firstEvent]);

        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.WriteEvents(
                [secondEvent],
                [Precondition.IsSubjectPristinePrecondition("/test")]
            )
        );

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }

    [Fact]
    public async Task SupportsTheIsSubjectOnEventIdPrecondition()
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

        _ = await client.WriteEvents([firstEvent]);

        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.WriteEvents(
                [secondEvent],
                [Precondition.IsSubjectOnEventIdPrecondition("/test", "1")]
            )
        );

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }

    private record struct EventData(int Value);
}
