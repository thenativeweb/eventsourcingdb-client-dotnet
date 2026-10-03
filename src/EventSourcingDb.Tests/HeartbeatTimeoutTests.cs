using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class HeartbeatTimeoutTests
{
    private const string Heartbeat = """{"type":"heartbeat","payload":{}}""";

    private static readonly TimeSpan _heartbeatTimeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void UsesAHeartbeatTimeoutOfThirtySecondsByDefault()
    {
        var client = new Client(new Uri("http://localhost:3000"), "secret");

        Assert.Equal(TimeSpan.FromSeconds(30), client.HeartbeatTimeout);
    }

    [Theory]
    [InlineData("observe-events")]
    [InlineData("run-eventql-query")]
    public async Task ThrowsAHeartbeatTimeoutIfNeitherAnEventNorAHeartbeatArrives(string endpoint)
    {
        await using var server = new NdjsonTestServer(async (sendLine, _) =>
        {
            await sendLine(Heartbeat);
        });
        using var httpClient = new HttpClient { BaseAddress = server.BaseUrl };
        var client = new Client(httpClient) { HeartbeatTimeout = _heartbeatTimeout };

        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<HeartbeatTimeoutException>(async () =>
        {
            await foreach (var _ in GetStream(client, endpoint, source.Token))
            {
            }
        });

        stopwatch.Stop();

        Assert.Equal("No event and no heartbeat arrived for 0.5 seconds.", exception.Message);

        // Timers may fire a few milliseconds early, depending on the resolution of the clock.
        Assert.InRange(stopwatch.Elapsed, _heartbeatTimeout - TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(5));

        await server.ConnectionClosed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("observe-events")]
    [InlineData("run-eventql-query")]
    public async Task KeepsTheStreamOpenWhileHeartbeatsArrive(string endpoint)
    {
        await using var server = new NdjsonTestServer(async (sendLine, token) =>
        {
            for (var i = 0; i < 20; i++)
            {
                await sendLine(Heartbeat);
                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }

            await sendLine(GetItemLine(endpoint, "0"));
        });
        using var httpClient = new HttpClient { BaseAddress = server.BaseUrl };
        var client = new Client(httpClient) { HeartbeatTimeout = _heartbeatTimeout };

        var items = new List<Event?>();
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stopwatch = Stopwatch.StartNew();

        await foreach (var item in GetStream(client, endpoint, source.Token))
        {
            items.Add(item);
            break;
        }

        stopwatch.Stop();

        var receivedItem = Assert.Single(items);
        Assert.NotNull(receivedItem);
        Assert.Equal("0", receivedItem.Id);
        Assert.True(stopwatch.Elapsed > _heartbeatTimeout * 3);
    }

    [Theory]
    [InlineData("observe-events")]
    [InlineData("run-eventql-query")]
    public async Task DeliversEventsThatArriveWithinTheTimeout(string endpoint)
    {
        // There are no heartbeats in between, so only the events keep the stream open.
        await using var server = new NdjsonTestServer(async (sendLine, token) =>
        {
            for (var i = 0; i < 5; i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), token);
                await sendLine(GetItemLine(endpoint, i.ToString()));
            }
        });
        using var httpClient = new HttpClient { BaseAddress = server.BaseUrl };
        var client = new Client(httpClient) { HeartbeatTimeout = _heartbeatTimeout };

        var items = new List<Event?>();
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await foreach (var item in GetStream(client, endpoint, source.Token))
        {
            items.Add(item);
            if (items.Count >= 5)
            {
                break;
            }
        }

        Assert.Equal(["0", "1", "2", "3", "4"], items.ConvertAll(item => item?.Id));
    }

    [Theory]
    [InlineData("observe-events")]
    [InlineData("run-eventql-query")]
    public async Task EndsTheStreamWithACancellationIfTheCallerCancels(string endpoint)
    {
        await using var server = new NdjsonTestServer(async (sendLine, _) =>
        {
            await sendLine(Heartbeat);
        });
        using var httpClient = new HttpClient { BaseAddress = server.BaseUrl };
        var client = new Client(httpClient) { HeartbeatTimeout = TimeSpan.FromSeconds(2) };

        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in GetStream(client, endpoint, source.Token))
            {
            }
        });
    }

    private static IAsyncEnumerable<Event?> GetStream(Client client, string endpoint, CancellationToken token)
    {
        return endpoint switch
        {
            "observe-events" => client.ObserveEventsAsync("/", new ObserveEventsOptions(Recursive: true), token),
            "run-eventql-query" => client.RunEventQlQueryAsync<Event>("FROM e IN events PROJECT INTO e", token),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unknown endpoint."),
        };
    }

    private static string GetItemLine(string endpoint, string id)
    {
        var type = endpoint == "observe-events" ? "event" : "row";

        return $$$"""{"type":"{{{type}}}","payload":{"specversion":"1.0","id":"{{{id}}}","time":"2026-10-03T12:00:00Z","source":"https://www.eventsourcingdb.io","subject":"/test","type":"io.eventsourcingdb.test","datacontenttype":"application/json","data":{"value":23},"hash":"","predecessorhash":""}}""";
    }
}
