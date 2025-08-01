using System;
using System.Linq;
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

        var writtenEvents = await client.WriteEventsAsync([eventCandidate]);

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

        var writtenEvents = await client.WriteEventsAsync([firstEvent, secondEvent]);

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

        _ = await client.WriteEventsAsync([firstEvent]);

        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.WriteEventsAsync(
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

        _ = await client.WriteEventsAsync([firstEvent]);

        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.WriteEventsAsync(
                [secondEvent],
                [Precondition.IsSubjectOnEventIdPrecondition("/test", "1")]
            )
        );

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }

    [Fact]
    public async Task SupportsTheIsEventQlTruePrecondition()
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

        _ = await client.WriteEventsAsync([firstEvent]);

        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.WriteEventsAsync(
                [secondEvent],
                [Precondition.IsEventQlTruePrecondition("FROM e IN events PROJECT INTO COUNT() == 0")]
            )
        );

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }

    [Fact]
    public async Task DeserializesEventDataCorrectly()
    {
        var client = _container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate]);

        var data = writtenEvents[0].GetData(typeof(EventData));

        Assert.IsType<EventData>(data);
        Assert.Equal(eventCandidate.Data, data);
    }

    [Fact]
    public async Task PassesErrorMessageInException()
    {
        var client = _container!.GetClient();

        const string invalidSubject = "test"; // Subjects must start with a slash

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: invalidSubject,
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writeEvents = async () => await client.WriteEventsAsync([eventCandidate]);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(writeEvents);

        Assert.Contains("subject", ex.Message);
    }

    private record struct EventData(int Value);
}
