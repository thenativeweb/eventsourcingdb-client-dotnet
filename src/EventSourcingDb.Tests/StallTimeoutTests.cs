using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.DependencyInjection;
using EventSourcingDb.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class StallTimeoutTests
{
    private static readonly TimeSpan _stallTimeout = TimeSpan.FromMilliseconds(500);

    // Ends a request that hangs, so that a test fails instead of waiting forever.
    private static readonly TimeSpan _guardTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] _apiVersions = ["v1", "v2"];

    public static TheoryData<string, string> AllRequests => Cross(_apiVersions, TestRequests.All);

    public static TheoryData<string, string> Streams => Cross(_apiVersions, TestRequests.Streams);

    public static TheoryData<string, string> StreamsWithHeartbeats => Cross(_apiVersions, ["observe-events", "run-eventql-query"]);

    [Fact]
    public void UsesAStallTimeoutOfThirtySecondsByDefault()
    {
        using var httpClient = new HttpClient();
        var options = new EventSourcingDbOptions { ApiToken = "secret", BaseUrl = new Uri("http://localhost:3000") };

        Assert.Equal(TimeSpan.FromSeconds(30), new ClientOptions().StallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.StallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), new Client(new Uri("http://localhost:3000"), "secret").StallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), new Client(httpClient).StallTimeout);
    }

    [Fact]
    public void UsesTheStallTimeoutOfTheClientOptions()
    {
        var client = new Client(
            new Uri("http://localhost:3000"),
            "secret",
            new ClientOptions { StallTimeout = TimeSpan.FromSeconds(5) }
        );

        Assert.Equal(TimeSpan.FromSeconds(5), client.StallTimeout);
    }

    [Fact]
    public void UsesTheStallTimeoutOfTheClientOptionsWithACustomHttpClient()
    {
        using var httpClient = new HttpClient();

        var client = new Client(httpClient, new ClientOptions { StallTimeout = TimeSpan.FromSeconds(5) }, null, null);

        Assert.Equal(TimeSpan.FromSeconds(5), client.StallTimeout);
        Assert.Throws<ArgumentNullException>(() => new Client(httpClient, null!, null, null));
    }

    [Fact]
    public void KeepsResolvingACustomHttpClientWithANullArgumentToTheConstructorWithoutClientOptions()
    {
        using var httpClient = new HttpClient();

        // These calls compiled before ClientOptions existed. They must neither become ambiguous nor
        // reach the constructor with ClientOptions, which would reject null options.
        Assert.Equal(TimeSpan.FromSeconds(30), new Client(httpClient, null).StallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), new Client(httpClient, null, null).StallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), new Client(httpClient, default).StallTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4_294_967_295)]
    public void RejectsAStallTimeoutThatATimerCannotWaitFor(double milliseconds)
    {
        var options = new ClientOptions { StallTimeout = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new Client(new Uri("http://localhost:3000"), "secret", options));
    }

    [Fact]
    public void DoesNotLetTheTimeoutOfItsHttpClientCutRequestsShort()
    {
        var client = new Client(new Uri("http://localhost:3000"), "secret");

        Assert.Equal(Timeout.InfiniteTimeSpan, client.HttpClient.Timeout);
    }

    [Fact]
    public async Task FailsWithAConnectionStalledExceptionIfTheProbeStalls()
    {
        await using var server = new StallingTestServer((_, _) => Task.CompletedTask);
        using var httpClient = new HttpClient { BaseAddress = server.BaseUrl };
        var client = new Client(httpClient, new ClientOptions { StallTimeout = _stallTimeout }, null, null);
        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<ConnectionStalledException>(() => client.PingAsync(guard.Token));
        stopwatch.Stop();

        Assert.Equal("Nothing arrived from the server for 0.5 seconds.", exception.Message);
        AssertStalledAfterTheStallTimeout(stopwatch.Elapsed);
    }

    [Theory]
    [MemberData(nameof(AllRequests))]
    public async Task FailsWithAConnectionStalledExceptionIfTheResponseHeadersDoNotArrive(string apiVersion, string request)
    {
        await using var server = new StallingTestServer((_, _) => Task.CompletedTask);
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, _stallTimeout);
        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, guard.Token));
        stopwatch.Stop();

        var stalledException = Assert.IsType<ConnectionStalledException>(UnwrapWrite(request, exception));
        Assert.Equal("Nothing arrived from the server for 0.5 seconds.", stalledException.Message);
        AssertStalledAfterTheStallTimeout(stopwatch.Elapsed);

        await server.ConnectionClosed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(AllRequests))]
    public async Task FailsWithAConnectionStalledExceptionIfTheResponseStallsAfterItsHeaders(string apiVersion, string request)
    {
        await using var server = new StallingTestServer(async (write, _) =>
        {
            await write(
                "HTTP/1.1 200 OK\r\n" +
                "Server: EventSourcingDB/0.0.0\r\n" +
                "Content-Type: application/json\r\n" +
                "Content-Length: 100\r\n" +
                "\r\n" +
                """{"type":"""
            );
        });
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, _stallTimeout);
        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, guard.Token));
        stopwatch.Stop();

        Assert.IsType<ConnectionStalledException>(UnwrapWrite(request, exception));
        AssertStalledAfterTheStallTimeout(stopwatch.Elapsed);

        await server.ConnectionClosed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Streams))]
    public async Task FailsWithAConnectionStalledExceptionIfAStreamStallsAfterAnItem(string apiVersion, string request)
    {
        await using var server = new NdjsonTestServer(async (sendLine, _) =>
        {
            await sendLine(TestRequests.FirstItemOf(apiVersion, request));
        });
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, _stallTimeout);
        var itemCount = 0;
        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<ConnectionStalledException>(async () =>
        {
            await foreach (var _ in TestRequests.GetStream(client, request, guard.Token))
            {
                itemCount++;
            }
        });
        stopwatch.Stop();

        Assert.Equal(1, itemCount);
        Assert.Equal("Nothing arrived from the server for 0.5 seconds.", exception.Message);
        AssertStalledAfterTheStallTimeout(stopwatch.Elapsed);

        await server.ConnectionClosed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(StreamsWithHeartbeats))]
    public async Task FailsWithAConnectionStalledExceptionIfNothingArrivesAfterAHeartbeat(string apiVersion, string request)
    {
        await using var server = new NdjsonTestServer(async (sendLine, _) =>
        {
            await sendLine(TestRequests.Heartbeat(apiVersion));
        });
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, _stallTimeout);
        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<ConnectionStalledException>(async () =>
        {
            await foreach (var _ in TestRequests.GetStream(client, request, guard.Token))
            {
            }
        });
        stopwatch.Stop();

        AssertStalledAfterTheStallTimeout(stopwatch.Elapsed);

        await server.ConnectionClosed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(StreamsWithHeartbeats))]
    public async Task KeepsAStreamOpenWhileHeartbeatsArrive(string apiVersion, string request)
    {
        await using var server = new NdjsonTestServer(async (sendLine, token) =>
        {
            for (var i = 0; i < 20; i++)
            {
                await sendLine(TestRequests.Heartbeat(apiVersion));
                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }

            await sendLine(TestRequests.FirstItemOf(apiVersion, request));
        });
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, _stallTimeout);
        using var guard = CreateGuard();

        var items = new List<Event?>();
        var stopwatch = Stopwatch.StartNew();

        await foreach (var item in TestRequests.GetStream(client, request, guard.Token))
        {
            items.Add(item as Event);
            break;
        }

        stopwatch.Stop();

        Assert.Equal("0", Assert.Single(items)?.Id);
        Assert.True(stopwatch.Elapsed > _stallTimeout * 3);
    }

    [Fact]
    public async Task KeepsAWriteOpenWhileHeartbeatsArrive()
    {
        await using var server = new NdjsonTestServer(async (sendLine, token) =>
        {
            for (var i = 0; i < 20; i++)
            {
                await sendLine(TestRequests.Heartbeat("v2"));
                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }

            await sendLine(TestRequests.Event("v2", "0"));
            await sendLine(TestRequests.End);
        });
        var client = ProbeAnsweringHandler.CreateClient("v2", server.BaseUrl, _stallTimeout);
        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        var writtenEvents = await client.WriteEventsAsync(
            [new EventCandidate("https://www.eventsourcingdb.io", "/test", "io.eventsourcingdb.test", new { value = 23 })],
            token: guard.Token
        );
        stopwatch.Stop();

        Assert.Equal("0", Assert.Single(writtenEvents).Id);
        Assert.True(stopwatch.Elapsed > _stallTimeout * 3);
    }

    [Theory]
    [MemberData(nameof(StreamsWithHeartbeats))]
    public async Task DeliversItemsThatArriveWithinTheTimeout(string apiVersion, string request)
    {
        // There are no heartbeats in between, so only the items keep the stream open.
        await using var server = new NdjsonTestServer(async (sendLine, token) =>
        {
            for (var i = 0; i < 5; i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), token);
                await sendLine(TestRequests.FirstItemOf(apiVersion, request).Replace("\"id\":\"0\"", $"\"id\":\"{i}\""));
            }
        });
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, _stallTimeout);
        using var guard = CreateGuard();

        var items = new List<Event?>();

        await foreach (var item in TestRequests.GetStream(client, request, guard.Token))
        {
            items.Add(item as Event);
            if (items.Count >= 5)
            {
                break;
            }
        }

        Assert.Equal(["0", "1", "2", "3", "4"], items.ConvertAll(item => item?.Id));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task KeepsASingleResponseAliveWhileItsBytesArrive(string apiVersion)
    {
        var body = apiVersion == "v1"
            ? """{"type":"io.eventsourcingdb.api.ping-received"}"""
            : """{"type":"event","data":{"type":"io.eventsourcingdb.api.ping-received"}}""";

        await using var server = new StallingTestServer(async (write, token) =>
        {
            await write(
                "HTTP/1.1 200 OK\r\n" +
                "Server: EventSourcingDB/0.0.0\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
                "\r\n"
            );

            foreach (var character in body)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), token);
                await write(character.ToString());
            }
        });
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, _stallTimeout);
        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        await client.PingAsync(guard.Token);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed > _stallTimeout * 3);
    }

    [Theory]
    [InlineData("v1", "read-events")]
    [InlineData("v1", "observe-events")]
    [InlineData("v2", "read-events")]
    [InlineData("v2", "observe-events")]
    public async Task KeepsAStreamAliveWhileTheBytesOfALineArrive(string apiVersion, string request)
    {
        // Each byte arrives on its own, so the line takes far longer than the stall timeout.
        await using var server = new StallingTestServer(async (write, token) =>
        {
            await write(
                "HTTP/1.1 200 OK\r\n" +
                "Server: EventSourcingDB/0.0.0\r\n" +
                "Content-Type: application/x-ndjson\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n"
            );

            foreach (var character in TestRequests.Event(apiVersion, "0") + "\n")
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), token);
                await write(NdjsonTestServer.Chunk(character.ToString()));
            }
        });
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, _stallTimeout);
        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        var items = new List<Event?>();
        await foreach (var item in TestRequests.GetStream(client, request, guard.Token))
        {
            items.Add(item as Event);
            break;
        }
        stopwatch.Stop();

        Assert.Equal("0", Assert.Single(items)?.Id);
        Assert.True(stopwatch.Elapsed > _stallTimeout * 3);
    }

    [Theory]
    [InlineData("v1", "ping", false)]
    [InlineData("v1", "ping", true)]
    [InlineData("v1", "read-events", false)]
    [InlineData("v1", "observe-events", true)]
    [InlineData("v2", "ping", false)]
    [InlineData("v2", "ping", true)]
    [InlineData("v2", "read-events", false)]
    [InlineData("v2", "observe-events", true)]
    public async Task EndsWithACancellationOfTheCallersTokenIfTheCallerCancelsFirst(string apiVersion, string request, bool sendHeaders)
    {
        await using var server = new StallingTestServer(async (write, _) =>
        {
            if (sendHeaders)
            {
                await write(
                    "HTTP/1.1 200 OK\r\n" +
                    "Server: EventSourcingDB/0.0.0\r\n" +
                    "Content-Type: application/json\r\n" +
                    "Content-Length: 100\r\n" +
                    "\r\n" +
                    """{"type":"""
                );
            }
        });
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, TimeSpan.FromSeconds(5));

        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = TestRequests.RunAsync(client, request, source.Token);
        await server.RequestReceived.WaitAsync(_guardTimeout, TestContext.Current.CancellationToken);
        await source.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        // Callers tell their own cancellation apart by its token, as with HttpClient.
        Assert.Equal(source.Token, exception.CancellationToken);
        Assert.IsNotType<ConnectionStalledException>(exception.InnerException);
    }

    [Fact]
    public async Task TakesTheStallTimeoutFromTheConfiguration()
    {
        await using var server = new StallingTestServer((_, _) => Task.CompletedTask);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", server.BaseUrl.ToString()),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", "secret"),
                    new KeyValuePair<string, string?>("EventSourcingDb:StallTimeout", "00:00:00.5")
                ]
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventSourcingDb(configuration);

        await using var serviceProvider = services.BuildServiceProvider();
        var client = (Client)serviceProvider.GetRequiredService<IClient>();

        Assert.Equal(_stallTimeout, client.StallTimeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.HttpClient.Timeout);

        using var guard = CreateGuard();

        await Assert.ThrowsAsync<ConnectionStalledException>(() => client.PingAsync(guard.Token));
    }

    [Fact]
    public void KeepsATimeoutConfiguredForTheHttpClientOfTheDependencyInjection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", "http://localhost:3000"),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", "secret")
                ]
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventSourcingDb(configuration)
            .ConfigureHttpClient(httpClient => httpClient.Timeout = TimeSpan.FromSeconds(10));

        using var serviceProvider = services.BuildServiceProvider();
        var client = (Client)serviceProvider.GetRequiredService<IClient>();

        Assert.Equal(TimeSpan.FromSeconds(10), client.HttpClient.Timeout);
    }

    [Fact]
    public void KeepsATimeoutConfiguredForAllHttpClientsOfTheDependencyInjection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", "http://localhost:3000"),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", "secret")
                ]
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(builder => builder.ConfigureHttpClient(httpClient => httpClient.Timeout = TimeSpan.FromSeconds(5)));
        services.AddEventSourcingDb(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var client = (Client)serviceProvider.GetRequiredService<IClient>();

        Assert.Equal(TimeSpan.FromSeconds(5), client.HttpClient.Timeout);
    }

    // A write that stalls may have been stored, so it fails with an OutcomeUnknownException that
    // holds the stall.
    private static Exception? UnwrapWrite(string request, Exception? exception)
    {
        if (Array.IndexOf(TestRequests.Writes, request) < 0)
        {
            return exception;
        }

        return Assert.IsType<OutcomeUnknownException>(exception).InnerException;
    }

    private static TheoryData<string, string> Cross(string[] apiVersions, string[] requests)
    {
        var data = new TheoryData<string, string>();

        foreach (var apiVersion in apiVersions)
        {
            foreach (var request in requests)
            {
                data.Add(apiVersion, request);
            }
        }

        return data;
    }

    private static CancellationTokenSource CreateGuard()
    {
        var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(_guardTimeout);

        return guard;
    }

    private static void AssertStalledAfterTheStallTimeout(TimeSpan elapsed)
    {
        // Timers may fire a few milliseconds early, depending on the resolution of the clock.
        Assert.InRange(elapsed, _stallTimeout - TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(5));
    }
}
