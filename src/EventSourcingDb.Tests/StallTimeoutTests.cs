using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
    private const string Event = """{"specversion":"1.0","id":"0","time":"2026-10-03T12:00:00Z","source":"https://www.eventsourcingdb.io","subject":"/test","type":"io.eventsourcingdb.test","datacontenttype":"application/json","data":{"value":23},"hash":"","predecessorhash":""}""";

    private static readonly TimeSpan _stallTimeout = TimeSpan.FromMilliseconds(500);

    // Ends a request that hangs, so that a test fails instead of waiting forever.
    private static readonly TimeSpan _guardTimeout = TimeSpan.FromSeconds(10);

    public static TheoryData<string> AllRequests =>
    [
        "ping",
        "verify-api-token",
        "write-events",
        "register-event-schema",
        "read-event-type",
        "read-events",
        "read-subjects",
        "read-event-types",
        "observe-events",
        "run-eventql-query",
    ];

    public static TheoryData<string> RequestsWithASingleResponse =>
    [
        "ping",
        "verify-api-token",
        "write-events",
        "register-event-schema",
        "read-event-type",
    ];

    public static TheoryData<string, string> StreamsWithoutHeartbeats => new()
    {
        { "read-events", $$"""{"type":"event","payload":{{Event}}}""" },
        { "read-subjects", """{"type":"subject","payload":{"subject":"/test"}}""" },
        { "read-event-types", """{"type":"eventType","payload":{"eventType":"io.eventsourcingdb.test","isPhantom":false,"schema":null}}""" },
    };

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

    [Fact]
    public async Task FailsAStalledRequestOfACustomHttpClientAfterTheStallTimeout()
    {
        await using var server = new StallingTestServer((_, _) => Task.CompletedTask);
        using var httpClient = new HttpClient { BaseAddress = server.BaseUrl };
        var client = new Client(httpClient, new ClientOptions { StallTimeout = _stallTimeout }, null, null);

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<TaskCanceledException>(() => RunAsync(client, "ping"));
        stopwatch.Stop();

        Assert.IsType<ConnectionStalledException>(exception.InnerException);
        AssertStalledAfterTheStallTimeout(stopwatch.Elapsed);
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

        Assert.Equal(Timeout.InfiniteTimeSpan, GetHttpClient(client).Timeout);
    }

    [Theory]
    [MemberData(nameof(AllRequests))]
    public async Task FailsWithATaskCanceledExceptionIfTheResponseHeadersDoNotArrive(string request)
    {
        await using var server = new StallingTestServer((_, _) => Task.CompletedTask);
        var client = new Client(server.BaseUrl, "secret", new ClientOptions { StallTimeout = _stallTimeout });

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<TaskCanceledException>(() => RunAsync(client, request));
        stopwatch.Stop();

        var innerException = Assert.IsType<ConnectionStalledException>(exception.InnerException);
        Assert.Equal("The request was canceled, since nothing arrived from the server for 0.5 seconds.", exception.Message);
        Assert.Equal("Nothing arrived from the server for 0.5 seconds.", innerException.Message);
        AssertStalledAfterTheStallTimeout(stopwatch.Elapsed);

        await server.ConnectionClosed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(RequestsWithASingleResponse))]
    public async Task FailsWithATaskCanceledExceptionIfASingleResponseStalls(string request)
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
        var client = new Client(server.BaseUrl, "secret", new ClientOptions { StallTimeout = _stallTimeout });

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<TaskCanceledException>(() => RunAsync(client, request));
        stopwatch.Stop();

        Assert.IsType<ConnectionStalledException>(exception.InnerException);
        AssertStalledAfterTheStallTimeout(stopwatch.Elapsed);

        await server.ConnectionClosed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(StreamsWithoutHeartbeats))]
    public async Task FailsWithAConnectionStalledExceptionIfAStreamStalls(string request, string line)
    {
        await using var server = new NdjsonTestServer(async (sendLine, _) =>
        {
            await sendLine(line);
        });
        var client = new Client(server.BaseUrl, "secret", new ClientOptions { StallTimeout = _stallTimeout });
        var itemCount = 0;
        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<ConnectionStalledException>(async () =>
        {
            await foreach (var _ in GetStream(client, request, guard.Token))
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

    [Fact]
    public async Task KeepsASingleResponseAliveWhileItsBytesArrive()
    {
        const string body = """{"type":"io.eventsourcingdb.api.ping-received"}""";

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
        var client = new Client(server.BaseUrl, "secret", new ClientOptions { StallTimeout = _stallTimeout });

        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        await client.PingAsync(guard.Token);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed > _stallTimeout * 3);
    }

    [Theory]
    [InlineData("read-events")]
    [InlineData("observe-events")]
    public async Task KeepsAStreamAliveWhileTheBytesOfALineArrive(string request)
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

            foreach (var character in $$"""{"type":"event","payload":{{Event}}}""" + "\n")
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), token);
                await write(NdjsonTestServer.Chunk(character.ToString()));
            }
        });
        var client = new Client(server.BaseUrl, "secret", new ClientOptions { StallTimeout = _stallTimeout });

        using var guard = CreateGuard();

        var stopwatch = Stopwatch.StartNew();
        var items = new List<Event?>();
        await foreach (var item in GetStream(client, request, guard.Token))
        {
            items.Add(item as Event);
            break;
        }
        stopwatch.Stop();

        Assert.Equal("0", Assert.Single(items)?.Id);
        Assert.True(stopwatch.Elapsed > _stallTimeout * 3);
    }

    [Fact]
    public async Task EndsWithACancellationIfTheCallerCancelsFirst()
    {
        await using var server = new StallingTestServer((_, _) => Task.CompletedTask);
        var client = new Client(server.BaseUrl, "secret", new ClientOptions { StallTimeout = TimeSpan.FromSeconds(5) });

        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PingAsync(source.Token));

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
        Assert.Equal(Timeout.InfiniteTimeSpan, GetHttpClient(client).Timeout);

        using var guard = CreateGuard();

        var exception = await Assert.ThrowsAsync<TaskCanceledException>(() => client.PingAsync(guard.Token));
        Assert.IsType<ConnectionStalledException>(exception.InnerException);
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

        Assert.Equal(TimeSpan.FromSeconds(10), GetHttpClient(client).Timeout);
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

    private static HttpClient GetHttpClient(Client client)
    {
        return (HttpClient)typeof(Client)
            .GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client)!;
    }

    private static async Task RunAsync(Client client, string request)
    {
        using var guard = CreateGuard();
        var token = guard.Token;

        switch (request)
        {
            case "ping":
                await client.PingAsync(token);
                break;
            case "verify-api-token":
                await client.VerifyApiTokenAsync(token);
                break;
            case "write-events":
                await client.WriteEventsAsync(
                    [new EventCandidate("https://www.eventsourcingdb.io", "/test", "io.eventsourcingdb.test", new { value = 23 })],
                    token: token
                );
                break;
            case "register-event-schema":
                await client.RegisterEventSchemaAsync("io.eventsourcingdb.test", JsonDocument.Parse("{}").RootElement, token);
                break;
            case "read-event-type":
                await client.ReadEventTypeAsync("io.eventsourcingdb.test", token);
                break;
            default:
                await foreach (var _ in GetStream(client, request, token))
                {
                }
                break;
        }
    }

    private static IAsyncEnumerable<object?> GetStream(Client client, string request, CancellationToken token)
    {
        return request switch
        {
            "read-events" => client.ReadEventsAsync("/", new ReadEventsOptions(Recursive: true), token),
            "read-subjects" => client.ReadSubjectsAsync("/", token),
            "read-event-types" => client.ReadEventTypesAsync(token),
            "observe-events" => client.ObserveEventsAsync("/", new ObserveEventsOptions(Recursive: true), token),
            "run-eventql-query" => client.RunEventQlQueryAsync<Event>("FROM e IN events PROJECT INTO e", token),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, "Unknown request."),
        };
    }
}
