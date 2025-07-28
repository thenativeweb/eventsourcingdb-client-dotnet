using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class ReadEventTypeTest : IAsyncLifetime
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
    public async Task FailsIfTheEventTypeDoesNotExist()
    {
        var client = _container!.GetClient();

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
        var client = _container!.GetClient();

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
        var client = _container!.GetClient();

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
