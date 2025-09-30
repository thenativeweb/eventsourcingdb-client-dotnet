using System;
using System.Net;
using System.Net.Http;
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

        try
        {
            await client.ReadEventTypeAsync("io.eventsourcingdb.nonexistent");
        }
        catch (HttpRequestException ex)
        {
            Assert.Equal("Unexpected status code.", ex.Message);
            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected exception: {ex.Message}");
        }
    }

    [Fact]
    public async Task FailsIfTheEventTypeIsMalformed()
    {
        var client = Container!.GetClient();

        try
        {
            await client.ReadEventTypeAsync("io.eventsourcingdb.malformed.");
        }
        catch (HttpRequestException ex)
        {
            Assert.Equal("Unexpected status code.", ex.Message);
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected exception: {ex.Message}");
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

        await client.WriteEventsAsync([firstEvent]);

        var eventType = await client.ReadEventTypeAsync("io.eventsourcingdb.test");
        Assert.Equal("io.eventsourcingdb.test", eventType.Type);
        Assert.False(eventType.IsPhantom);
        Assert.Null(eventType.Schema);
    }

    private record struct EventData(int Value);
}
