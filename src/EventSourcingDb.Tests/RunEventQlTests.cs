using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class RunEventQlTests : IAsyncLifetime
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
    public async Task ReadsNoRowsIfTheQueryDoesNotReturnAnyRows()
    {
        var client = _container!.GetClient();

        var didReadRows = false;
        await foreach (var _ in client.RunEventQlQueryAsync("FROM e IN events PROJECT INTO e"))
        {
            didReadRows = true;
        }

        Assert.False(didReadRows);
    }

    [Fact]
    public async Task ReadsAllRowsTheQueryReturns()
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

        var rowsRead = new List<JsonElement>();
        await foreach (var row in client.RunEventQlQueryAsync("FROM e IN events PROJECT INTO e"))
        {
            rowsRead.Add(row);
        }

        var deserializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Assert.Collection(rowsRead,
            row =>
            {
                var eventData = row.Deserialize<Event>(deserializerOptions);
                Assert.NotNull(eventData);
                Assert.Equal("0", eventData.Id);
                Assert.Equal(firstData, eventData.GetData<EventData>());
            },
            row =>
            {
                var eventData = row.Deserialize<Event>(deserializerOptions);
                Assert.NotNull(eventData);
                Assert.Equal("1", eventData.Id);
                Assert.Equal(secondData, eventData.GetData<EventData>());
            }
        );
    }

    private record struct EventData(int Value);
}
