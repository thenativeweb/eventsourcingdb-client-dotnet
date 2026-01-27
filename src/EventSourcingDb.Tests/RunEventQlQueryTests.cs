using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class RunEventQlQueryTests : EventSourcingDbTests
{
    [Fact]
    public async Task ReadsNoRowsIfTheQueryDoesNotReturnAnyRows()
    {
        var client = Container!.GetClient();

        var rowsRead = await client
            .RunEventQlQueryAsync<Event>("FROM e IN events PROJECT INTO e", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(rowsRead);
    }

    [Fact]
    public async Task ReadsAllRowsTheQueryReturnsEvents()
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

        await client.WriteEventsAsync([firstEvent, secondEvent], token: TestContext.Current.CancellationToken);

        var rowsRead = await client
            .RunEventQlQueryAsync<Event>("FROM e IN events PROJECT INTO e", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Collection(rowsRead,
            row =>
            {
                Assert.NotNull(row);
                Assert.Equal("0", row.Id);
                Assert.Equal(firstData, row.GetData<EventData>());
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
        List<EventCandidate> candidates = [firstEvent, secondEvent];

        await client.WriteEventsAsync(candidates, token: TestContext.Current.CancellationToken);

        const string query =
            "FROM e IN events " +
            "WHERE e.type == \"io.eventsourcingdb.test\"" +
            "PROJECT INTO { average: AVG(e.data.value), count: COUNT() } ";
        var rowsRead = await client
            .RunEventQlQueryAsync<EventDataAggregation>(query, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        var aggregation = Assert.Single(rowsRead);
        Assert.Equal(32.5, aggregation.Average);
        Assert.Equal(2, aggregation.Count);
    }

    [Fact]
    public async Task ThrowsOnDeserializationError()
    {
        var client = Container!.GetClient();

        var eventData = new EventData(23);
        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: eventData
        );

        await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidValueException>(async () =>
        {
            await foreach (var _ in client.RunEventQlQueryAsync<string>("FROM e IN events PROJECT INTO e", TestContext.Current.CancellationToken))
            {
                throw new NotImplementedException();
            }
        });
    }

    [Fact]
    public async Task AllowsNullResults()
    {
        var client = Container!.GetClient();

        var eventData = new EventData(23);
        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: eventData
        );

        await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        var rowsRead = await client
            .RunEventQlQueryAsync<string?>("FROM e IN events PROJECT INTO null", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(rowsRead);
        Assert.Null(rowsRead[0]);
    }

    private record struct EventData(int Value);

    private record struct EventDataAggregation(float Average, int Count);
}
