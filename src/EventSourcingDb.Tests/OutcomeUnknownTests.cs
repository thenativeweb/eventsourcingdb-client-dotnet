using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

// A write is certainly not stored only if the connection could not be established at all, or if
// the server explicitly refused it. Every other failure of writing events or registering an event
// schema leaves open whether it was stored, and fails with an OutcomeUnknownException.
public sealed class OutcomeUnknownTests
{
    private static readonly TimeSpan _guardTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] _apiVersions = ["v1", "v2"];

    public static TheoryData<string, string> Writes => Cross(_apiVersions, TestRequests.Writes);

    public static TheoryData<string, string> WritesOfTheV2Api => Cross(["v2"], TestRequests.Writes);

    // The v1 API answers a registration without a body, so only these writes read their answer.
    public static TheoryData<string, string> WritesThatReadTheirAnswer => new()
    {
        { "v1", "write-events" },
        { "v2", "write-events" },
        { "v2", "register-event-schema" },
    };

    public static TheoryData<string, string, HttpStatusCode> RefusingStatuses => new()
    {
        // The v2 API refuses a write that it does not store with these statuses.
        { "v2", "write-events", HttpStatusCode.BadRequest },
        { "v2", "write-events", HttpStatusCode.Unauthorized },
        { "v2", "write-events", HttpStatusCode.RequestTimeout },
        { "v2", "write-events", HttpStatusCode.RequestEntityTooLarge },
        { "v2", "write-events", HttpStatusCode.TooManyRequests },
        { "v2", "write-events", HttpStatusCode.ServiceUnavailable },
        { "v2", "register-event-schema", HttpStatusCode.BadRequest },
        { "v2", "register-event-schema", HttpStatusCode.Unauthorized },
        { "v2", "register-event-schema", HttpStatusCode.RequestTimeout },
        { "v2", "register-event-schema", HttpStatusCode.RequestEntityTooLarge },
        { "v2", "register-event-schema", HttpStatusCode.TooManyRequests },
        { "v2", "register-event-schema", HttpStatusCode.ServiceUnavailable },
        // The v1 API refuses a write that it does not store with any status of 4xx, 503, or 507.
        { "v1", "write-events", HttpStatusCode.BadRequest },
        { "v1", "write-events", HttpStatusCode.Unauthorized },
        { "v1", "write-events", HttpStatusCode.NotFound },
        { "v1", "write-events", HttpStatusCode.Conflict },
        { "v1", "write-events", HttpStatusCode.RequestEntityTooLarge },
        { "v1", "write-events", HttpStatusCode.TooManyRequests },
        { "v1", "write-events", HttpStatusCode.ServiceUnavailable },
        { "v1", "write-events", HttpStatusCode.InsufficientStorage },
        { "v1", "register-event-schema", HttpStatusCode.BadRequest },
        { "v1", "register-event-schema", HttpStatusCode.Unauthorized },
        { "v1", "register-event-schema", HttpStatusCode.Conflict },
        { "v1", "register-event-schema", HttpStatusCode.ServiceUnavailable },
        { "v1", "register-event-schema", HttpStatusCode.InsufficientStorage },
    };

    public static TheoryData<string, string, HttpStatusCode> StatusesOfUnknownOutcome => new()
    {
        { "v2", "write-events", HttpStatusCode.InternalServerError },
        { "v2", "write-events", HttpStatusCode.BadGateway },
        { "v2", "write-events", HttpStatusCode.GatewayTimeout },
        { "v2", "write-events", HttpStatusCode.NotFound },
        { "v2", "write-events", HttpStatusCode.Conflict },
        { "v2", "write-events", HttpStatusCode.InsufficientStorage },
        { "v2", "register-event-schema", HttpStatusCode.InternalServerError },
        { "v2", "register-event-schema", HttpStatusCode.BadGateway },
        { "v2", "register-event-schema", HttpStatusCode.GatewayTimeout },
        { "v1", "write-events", HttpStatusCode.InternalServerError },
        { "v1", "write-events", HttpStatusCode.BadGateway },
        { "v1", "write-events", HttpStatusCode.GatewayTimeout },
        { "v1", "register-event-schema", HttpStatusCode.InternalServerError },
        { "v1", "register-event-schema", HttpStatusCode.BadGateway },
        { "v1", "register-event-schema", HttpStatusCode.GatewayTimeout },
    };

    public static TheoryData<string, string, string> RefusingDomainErrors => Cross(
        ["v2"],
        TestRequests.Writes,
        [
            ErrorCodes.PreconditionViolated,
            ErrorCodes.EventSchemaViolated,
            ErrorCodes.EventSchemaAlreadyRegistered,
            ErrorCodes.LicenseInvalid,
            ErrorCodes.StoreReadOnly,
            ErrorCodes.DiskFull,
            ErrorCodes.IndexesRebuilding,
            ErrorCodes.ServerShuttingDown,
            ErrorCodes.SubjectMalformed,
        ]
    );

    public static TheoryData<string, string, HttpRequestError> ConnectionErrors => Cross(
        _apiVersions,
        TestRequests.Writes,
        [HttpRequestError.NameResolutionError, HttpRequestError.ConnectionError, HttpRequestError.SecureConnectionError]
    );

    public static TheoryData<string, string, HttpRequestError> ErrorsAfterConnecting => Cross(
        _apiVersions,
        TestRequests.Writes,
        [HttpRequestError.ResponseEnded, HttpRequestError.InvalidResponse, HttpRequestError.HttpProtocolError, HttpRequestError.ProxyTunnelError, HttpRequestError.Unknown]
    );

    [Theory]
    [MemberData(nameof(RefusingDomainErrors))]
    public async Task ReportsAWriteThatTheV2ApiRefusesWithinItsAnswerAsNotStored(string apiVersion, string request, string code)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Ndjson(DomainError(code), TestRequests.End));

        var exception = await RunAsync(server, request);

        var dbApiException = Assert.IsType<DbApiException>(exception);
        Assert.Equal(code, dbApiException.Code);
        Assert.Equal(HttpStatusCode.OK, dbApiException.Status);
    }

    [Theory]
    [MemberData(nameof(WritesOfTheV2Api))]
    public async Task ReportsAWriteThatTheV2ApiRefusesAfterHeartbeatsAsNotStored(string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Ndjson(
            TestRequests.Heartbeat(apiVersion),
            TestRequests.Heartbeat(apiVersion),
            DomainError(ErrorCodes.PreconditionViolated),
            TestRequests.End
        ));

        var exception = await RunAsync(server, request);

        Assert.Equal(ErrorCodes.PreconditionViolated, Assert.IsType<DbApiException>(exception).Code);
    }

    [Theory]
    [MemberData(nameof(WritesOfTheV2Api))]
    public async Task ReportsAWriteThatFailsWithStoreFailedAsOfUnknownOutcome(string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Ndjson(DomainError(ErrorCodes.StoreFailed), TestRequests.End));

        var exception = await RunAsync(server, request);

        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.Equal(ErrorCodes.StoreFailed, Assert.IsType<DbApiException>(outcomeUnknownException.InnerException).Code);
    }

    [Theory]
    [MemberData(nameof(RefusingStatuses))]
    public async Task ReportsAWriteThatTheServerRefusesWithAStatusAsNotStored(string apiVersion, string request, HttpStatusCode status)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(status, "refused\n"));

        var exception = await RunAsync(server, request);

        Assert.Equal(status, Assert.IsType<DbApiException>(exception).Status);
    }

    [Theory]
    [MemberData(nameof(StatusesOfUnknownOutcome))]
    public async Task ReportsAWriteThatFailsWithAnyOtherStatusAsOfUnknownOutcome(string apiVersion, string request, HttpStatusCode status)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(status, "failed\n"));

        var exception = await RunAsync(server, request);

        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.Equal(status, Assert.IsType<DbApiException>(outcomeUnknownException.InnerException).Status);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteWhoseAnswerIsNotFromEventSourcingDbAsOfUnknownOutcome(string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Respond(HttpStatusCode.BadRequest, "bad request", "text/plain", isFromEventSourcingDb: false));

        var exception = await RunAsync(server, request);

        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.IsType<InvalidOperationException>(outcomeUnknownException.InnerException);
    }

    [Theory]
    [MemberData(nameof(WritesThatReadTheirAnswer))]
    public async Task ReportsAWriteWhoseAnswerCannotBeReadAsOfUnknownOutcome(string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Json(HttpStatusCode.OK, "<html>"));

        var exception = await RunAsync(server, request);

        Assert.IsType<OutcomeUnknownException>(exception);
    }

    [Theory]
    [MemberData(nameof(WritesOfTheV2Api))]
    public async Task ReportsAWriteWhoseStreamEndsWithoutItsEndItemAsOfUnknownOutcome(string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Ndjson(
            TestRequests.Heartbeat(apiVersion),
            TestRequests.Event(apiVersion, "0")
        ));

        var exception = await RunAsync(server, request);

        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        var inner = Assert.IsType<HttpIOException>(outcomeUnknownException.InnerException);
        Assert.Equal(HttpRequestError.ResponseEnded, inner.HttpRequestError);
    }

    [Theory]
    [MemberData(nameof(ConnectionErrors))]
    public async Task ReportsAWriteWhoseConnectionCannotBeEstablishedAsNotStored(string apiVersion, string request, HttpRequestError error)
    {
        var server = FakeServer.ForApi(apiVersion, _ => throw new HttpRequestException(error, "failed to connect"));

        var exception = await RunAsync(server, request);

        Assert.Equal(error, Assert.IsType<HttpRequestException>(exception).HttpRequestError);
    }

    [Theory]
    [MemberData(nameof(ErrorsAfterConnecting))]
    public async Task ReportsAWriteThatFailsAfterConnectingAsOfUnknownOutcome(string apiVersion, string request, HttpRequestError error)
    {
        var server = FakeServer.ForApi(apiVersion, _ => throw new HttpRequestException(error, "failed after connecting"));

        var exception = await RunAsync(server, request);

        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.Equal(error, Assert.IsType<HttpRequestException>(outcomeUnknownException.InnerException).HttpRequestError);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteThatIsCancelledBeforeItIsSentAsNotStored(string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, sentRequest => sentRequest.Path switch
        {
            "/api/v1/ping" => FakeServer.Json(HttpStatusCode.OK, """{"type":"io.eventsourcingdb.api.ping-received"}"""),
            FakeServer.ProbePath => FakeServer.Json(HttpStatusCode.OK, """{"type":"event","data":{"type":"io.eventsourcingdb.api.ping-received"}}"""),
            _ => throw new InvalidOperationException("No write expected."),
        });
        var client = server.CreateClient();

        // The ping lets the client find out the API first, so that the write is the next request.
        await client.PingAsync(TestContext.Current.CancellationToken);
        var requestCount = server.Requests.Count;

        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, source.Token));

        Assert.Equal(source.Token, Assert.IsAssignableFrom<OperationCanceledException>(exception).CancellationToken);
        Assert.Equal(requestCount, server.Requests.Count);
    }

    // The following cases run over real connections, to a server that answers the probe of the
    // client in the process and every other request over the network.

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteThatStallsAsOfUnknownOutcome(string apiVersion, string request)
    {
        await using var server = new StallingTestServer((_, _) => Task.CompletedTask);
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, TimeSpan.FromMilliseconds(200));

        var exception = await RunAsync(client, request);

        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.IsType<ConnectionStalledException>(outcomeUnknownException.InnerException);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteWhoseConnectionBreaksAfterSendingAsOfUnknownOutcome(string apiVersion, string request)
    {
        await using var server = new StallingTestServer((_, _) => Task.CompletedTask, closeAfterResponding: true);
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, TimeSpan.FromSeconds(5));

        var exception = await RunAsync(client, request);

        Assert.IsType<OutcomeUnknownException>(exception);
        Assert.True(server.RequestReceived.IsCompletedSuccessfully);
    }

    [Theory]
    [MemberData(nameof(WritesOfTheV2Api))]
    public async Task ReportsAWriteWhoseConnectionBreaksWithinTheStreamAsOfUnknownOutcome(string apiVersion, string request)
    {
        await using var server = new StallingTestServer(async (write, _) =>
        {
            await write(
                "HTTP/1.1 200 OK\r\n" +
                "Server: EventSourcingDB/0.0.0\r\n" +
                "Content-Type: application/x-ndjson\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                NdjsonTestServer.Chunk(TestRequests.Heartbeat(apiVersion) + "\n")
            );
        }, closeAfterResponding: true);
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, TimeSpan.FromSeconds(5));

        var exception = await RunAsync(client, request);

        Assert.IsType<OutcomeUnknownException>(exception);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteThatTheCallerCancelsAfterSendingAsOfUnknownOutcome(string apiVersion, string request)
    {
        await using var server = new StallingTestServer((_, _) => Task.CompletedTask);
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, server.BaseUrl, TimeSpan.FromSeconds(5));
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = TestRequests.RunAsync(client, request, source.Token);
        await server.RequestReceived.WaitAsync(_guardTimeout, TestContext.Current.CancellationToken);
        await source.CancelAsync();

        var exception = await Record.ExceptionAsync(() => running);

        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        var canceledException = Assert.IsAssignableFrom<OperationCanceledException>(outcomeUnknownException.InnerException);
        Assert.Equal(source.Token, canceledException.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteWhoseConnectionIsRefusedAsNotStored(string apiVersion, string request)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var client = ProbeAnsweringHandler.CreateClient(apiVersion, new Uri($"http://127.0.0.1:{port}"), TimeSpan.FromSeconds(5));

        var exception = await RunAsync(client, request);

        Assert.Equal(HttpRequestError.ConnectionError, Assert.IsType<HttpRequestException>(exception).HttpRequestError);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteWhoseHostCannotBeResolvedAsNotStored(string apiVersion, string request)
    {
        // The top-level domain .invalid never resolves, see RFC 2606.
        var client = ProbeAnsweringHandler.CreateClient(apiVersion, new Uri("http://eventsourcingdb.invalid"), TimeSpan.FromSeconds(5));

        var exception = await RunAsync(client, request);

        Assert.Equal(HttpRequestError.NameResolutionError, Assert.IsType<HttpRequestException>(exception).HttpRequestError);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteWhoseTlsConnectionCannotBeEstablishedAsNotStored(string apiVersion, string request)
    {
        // The server answers the TLS handshake with plain HTTP, so the handshake fails before the
        // request is sent.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var tcpClient = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = tcpClient.GetStream();
            await stream.WriteAsync("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
            tcpClient.Client.Shutdown(SocketShutdown.Both);
        }, TestContext.Current.CancellationToken);

        var client = ProbeAnsweringHandler.CreateClient(apiVersion, new Uri($"https://127.0.0.1:{port}"), TimeSpan.FromSeconds(5));

        var exception = await RunAsync(client, request);
        await serving;

        Assert.Equal(HttpRequestError.SecureConnectionError, Assert.IsType<HttpRequestException>(exception).HttpRequestError);
    }

    private static string DomainError(string code)
    {
        return TestRequests.Item("v2", "error", """{"code":"CODE","description":"refused","details":{}}""".Replace("CODE", code, StringComparison.Ordinal));
    }

    private static Task<Exception?> RunAsync(FakeServer server, string request)
    {
        return RunAsync(server.CreateClient(), request);
    }

    private static async Task<Exception?> RunAsync(IClient client, string request)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(_guardTimeout);

        return await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, guard.Token));
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

    private static TheoryData<string, string, T> Cross<T>(string[] apiVersions, string[] requests, T[] values)
    {
        var data = new TheoryData<string, string, T>();

        foreach (var apiVersion in apiVersions)
        {
            foreach (var request in requests)
            {
                foreach (var value in values)
                {
                    data.Add(apiVersion, request, value);
                }
            }
        }

        return data;
    }
}
