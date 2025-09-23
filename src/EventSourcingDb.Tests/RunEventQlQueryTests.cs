using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class RunEventQlQueryTests : IAsyncLifetime
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
        await foreach (var _ in client.RunEventQlQueryAsync<Event>("FROM e IN events PROJECT INTO e"))
        {
            didReadRows = true;
        }

        Assert.False(didReadRows);
    }

    [Fact]
    public async Task ReadsAllRowsTheQueryReturnsEvents()
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

        var rowsRead = new List<Event?>();
        await foreach (var row in client.RunEventQlQueryAsync<Event>("FROM e IN events PROJECT INTO e"))
        {
            rowsRead.Add(row);
        }

        Assert.Collection(rowsRead,
            @event =>
            {
                Assert.NotNull(@event);
                Assert.Equal("0", @event.Id);
                Assert.Equal(firstData, @event.GetData<EventData>());
            },
            row =>
            {
                Assert.NotNull(row);
                Assert.Equal("1", row.Id);
                Assert.Equal(secondData, row.GetData<EventData>());
            }
        );
    }

    [Fact]
    public async Task ReadsAllRowsTheQueryReturnsAggregation()
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
        List<EventCandidate> candidates = [firstEvent, secondEvent];

        await client.WriteEventsAsync(candidates);

        var rowsRead = new List<EventDataAggregation>();
        const string query =
            "FROM e IN events " +
            "WHERE e.type == \"io.eventsourcingdb.test\"" +
            "PROJECT INTO { average: AVG(e.data.value), count: COUNT() } ";
        await foreach (var row in client.RunEventQlQueryAsync<EventDataAggregation>(query))
        {
            rowsRead.Add(row);
        }

        var aggregation = Assert.Single(rowsRead);
        Assert.Equal(32.5, aggregation.Average);
        Assert.Equal(2, aggregation.Count);
    }

    [Fact]
    public async Task ThrowsOnDeserializationError()
    {
        var client = _container!.GetClient();

        var eventData = new EventData(23);
        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: eventData
        );

        await client.WriteEventsAsync([eventCandidate]);

        await Assert.ThrowsAsync<InvalidValueException>(async () =>
        {
            await foreach (var _ in client.RunEventQlQueryAsync<string>("FROM e IN events PROJECT INTO e"))
            {
                throw new NotImplementedException();
            }
        });
    }

    [Fact]
    public async Task AllowsNullResults()
    {
        var client = _container!.GetClient();

        var eventData = new EventData(23);
        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: eventData
        );

        await client.WriteEventsAsync([eventCandidate]);

        var rowsRead = new List<string?>();
        await foreach (var row in client.RunEventQlQueryAsync<string?>("FROM e IN events PROJECT INTO null"))
        {
            rowsRead.Add(row);
        }

        Assert.Single(rowsRead);
        Assert.Null(rowsRead[0]);
    }

    private record struct EventData(int Value);

    private record struct EventDataAggregation(float Average, int Count);
}
