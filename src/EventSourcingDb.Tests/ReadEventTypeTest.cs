using System.Net;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class ReadEventTypeTest : EventSourcingDbTests
{
    [Fact]
    public async Task FailsIfTheEventTypeDoesNotExist()
    {
        var client = Container!.GetClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(() =>
            client.ReadEventTypeAsync("io.eventsourcingdb.nonexistent", TestContext.Current.CancellationToken)
        );

        // The v1 API refuses it with 404, the v2 API reports it within its answer.
        Assert.Equal(ErrorCodes.EventTypeUnknown, exception.Code);
        Assert.Contains(exception.Status, new[] { HttpStatusCode.NotFound, HttpStatusCode.OK });
    }

    [Fact]
    public async Task FailsIfTheEventTypeIsMalformed()
    {
        var client = Container!.GetClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(() =>
            client.ReadEventTypeAsync("io.eventsourcingdb.malformed.", TestContext.Current.CancellationToken)
        );

        // The v1 API refuses it with 400 and no code, the v2 API reports it within its answer.
        if (exception.Status is HttpStatusCode.OK)
        {
            Assert.Equal(ErrorCodes.EventTypeMalformed, exception.Code);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, exception.Status);
            Assert.Null(exception.Code);
        }
    }

    [Fact]
    public async Task ReadsAnExistingEventType()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );

        await client.WriteEventsAsync([firstEvent], token: TestContext.Current.CancellationToken);

        var eventType = await client.ReadEventTypeAsync("io.eventsourcingdb.test", TestContext.Current.CancellationToken);
        Assert.Equal("io.eventsourcingdb.test", eventType.Type);
        Assert.True(eventType.HasEvents);
        Assert.Null(eventType.Schema);
    }

    private record struct EventData(int Value);
}
