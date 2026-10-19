using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace EventSourcingDb.Tests;

// A handler that sends a write again, such as a retry policy, can have the first attempt stored,
// lose its answer, and have the second one stored as well, or refused, for example because the
// precondition no longer holds after the first. So the client refuses to send a write a second
// time, and reports it as of unknown outcome. Only a handler that sends the write from a buffer
// or a copy of its own can still send it again, which the client cannot see, so once the write
// may have reached the server more than once, no refusal tells that it was not stored.
public sealed class RetriedWriteTests
{
    private const string ApiToken = "secret";

    private const string SentAgainMessage = "Something in the HTTP pipeline, for example a handler that retries requests, tried to send the write a second time, which the client SDK refused, so that it cannot be stored twice.";

    private static readonly string[] _apiVersions = ["v1", "v2"];

    // Where the standard resilience handler is added: for all clients, as the service defaults of
    // .NET Aspire do, on the builder of the registration for dependency injection, or to an
    // HttpClient that the application passes to the client.
    private static readonly string[] _setups = ["for-all-clients", "for-the-client", "custom-http-client"];

    public static TheoryData<string, string> Writes
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var apiVersion in _apiVersions)
            {
                foreach (var request in TestRequests.Writes)
                {
                    data.Add(apiVersion, request);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string, string> WritesInEverySetup
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var apiVersion in _apiVersions)
            {
                foreach (var request in TestRequests.Writes)
                {
                    foreach (var setup in _setups)
                    {
                        data.Add(apiVersion, request, setup);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task RefusesToSendAWriteThatAHandlerSendsAgain(string apiVersion, string request)
    {
        var server = new FirstAttemptLostServer(apiVersion, bufferTheBody: false);
        var client = new Client(new HttpClient(new RetryOnceHandler(server)) { BaseAddress = new Uri("http://esdb.test") });

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.Equal(1, server.Attempts);
        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        var refusal = Assert.IsType<NotSupportedException>(outcomeUnknownException.InnerException);
        Assert.Equal(SentAgainMessage, refusal.Message);
        Assert.Equal($"The outcome of the write is unknown, it may have been stored: {SentAgainMessage}", outcomeUnknownException.Message);
    }

    // The client sees how the first attempt failed only if sending its body failed. Then that
    // failure is the cause, rather than the refusal of the second attempt.
    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsHowSendingTheBodyFailedIfAHandlerSendsTheWriteAgain(string apiVersion, string request)
    {
        var client = new Client(new HttpClient(new RetryOnceHandler(new BrokenWhileSendingServer(apiVersion))) { BaseAddress = new Uri("http://esdb.test") });

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        var cause = Assert.IsType<IOException>(outcomeUnknownException.InnerException);
        Assert.Equal("The connection broke while the write was sent.", cause.Message);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteThatAHandlerSentAgainFromABufferAsOfUnknownOutcomeAlthoughTheServerRefusesTheRetry(string apiVersion, string request)
    {
        // Reading the body into a buffer lets every later attempt copy it from there, so the
        // attempts can neither be counted nor refused any more.
        var server = new FirstAttemptLostServer(apiVersion, bufferTheBody: true);
        var client = new Client(new HttpClient(new RetryOnceHandler(server)) { BaseAddress = new Uri("http://esdb.test") });

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.Equal(2, server.Attempts);
        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.IsType<DbApiException>(outcomeUnknownException.InnerException);
    }

    // The first attempt reaches the server, which stores it, and then the connection breaks
    // before the answer arrives. The standard resilience handler retries writes unless told
    // otherwise, so it would send the write a second time, and have it stored twice.
    [Theory]
    [MemberData(nameof(WritesInEverySetup))]
    public async Task RefusesToSendAWriteThatTheStandardResilienceHandlerSendsAgain(string apiVersion, string request, string setup)
    {
        var server = CreateServer(apiVersion, isFirstWriteLost: true);
        await using var listener = new FakeServerListener(server);
        await using var serviceProvider = CreateServiceProvider(setup, listener.BaseUrl, options =>
        {
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.UseJitter = false;
        });
        var client = CreateClient(setup, serviceProvider);

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.Single(server.Requests, IsWrite);
        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        var refusal = Assert.IsType<NotSupportedException>(outcomeUnknownException.InnerException);
        Assert.Equal(SentAgainMessage, refusal.Message);
    }

    [Theory]
    [MemberData(nameof(WritesInEverySetup))]
    public async Task SendsAWriteOnlyOnceIfTheStandardResilienceHandlerIsConfiguredAsTheReadmeSays(string apiVersion, string request, string setup)
    {
        var server = CreateServer(apiVersion, isFirstWriteLost: true);
        await using var listener = new FakeServerListener(server);
        await using var serviceProvider = CreateServiceProvider(setup, listener.BaseUrl, options =>
        {
            options.Retry.DisableForUnsafeHttpMethods();
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.UseJitter = false;
        });
        var client = CreateClient(setup, serviceProvider);

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.Single(server.Requests, IsWrite);
        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.IsType<HttpRequestException>(outcomeUnknownException.InnerException);
    }

    [Theory]
    [MemberData(nameof(WritesInEverySetup))]
    public async Task WritesOnceWithTheStandardResilienceHandlerIfTheFirstAttemptSucceeds(string apiVersion, string request, string setup)
    {
        var server = CreateServer(apiVersion, isFirstWriteLost: false);
        await using var listener = new FakeServerListener(server);
        await using var serviceProvider = CreateServiceProvider(setup, listener.BaseUrl, options =>
        {
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.UseJitter = false;
        });
        var client = CreateClient(setup, serviceProvider);

        await TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken);

        Assert.Single(server.Requests, IsWrite);
    }

    // A stream of the body goes to whoever reads it rather than to a connection, so a handler
    // that reads the body as a stream, such as one that logs it, does not keep the client from
    // sending it.
    [Theory]
    [MemberData(nameof(Writes))]
    public async Task SendsAWriteWhoseBodyAHandlerReadsAsAStreamBeforeSendingIt(string apiVersion, string request)
    {
        var server = CreateServer(apiVersion, isFirstWriteLost: false);
        var client = new Client(new HttpClient(new StreamReadingHandler(server)) { BaseAddress = new Uri("http://esdb.test") });

        await TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken);

        Assert.Single(server.Requests, IsWrite);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteWhoseBodyAHandlerBufferedAsOfUnknownOutcomeAlthoughTheServerRefusesIt(string apiVersion, string request)
    {
        // Once a handler has read the body into a buffer, the client cannot tell how often it was
        // sent, so it leaves the outcome open, even for a refusal.
        var server = FakeServer.ForApi(apiVersion, _ => Refusal(apiVersion));
        var client = new Client(new HttpClient(new BufferingHandler(server)) { BaseAddress = new Uri("http://esdb.test") });

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.IsType<DbApiException>(outcomeUnknownException.InnerException);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteThatWasSentOnceAndRefusedAsNotStored(string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, _ => Refusal(apiVersion));
        var client = server.CreateClient();

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.IsType<DbApiException>(exception);
    }

    private static HttpResponseMessage Refusal(string apiVersion)
    {
        return apiVersion == "v1"
            ? FakeServer.Text(HttpStatusCode.Conflict, "state conflict: precondition failed\n")
            : FakeServer.Ndjson(
                TestRequests.Item("v2", "error", """{"code":"https://docs.eventsourcingdb.io/errors/precondition-violated","description":"precondition does not hold","details":{}}"""),
                TestRequests.End
            );
    }

    // The answer of a write that the server stored. The v1 API answers a registration without
    // reading the body of its answer, so the same answer does for both writes.
    private static HttpResponseMessage Stored(string apiVersion)
    {
        return apiVersion == "v1"
            ? FakeServer.Json(HttpStatusCode.OK, $"[{TestRequests.EventJson}]")
            : FakeServer.Ndjson(TestRequests.Event("v2", "0"), TestRequests.End);
    }

    // Besides the write, the client sends only the probe.
    private static bool IsWrite(FakeRequest request) => !FakeServer.IsProbe(request);

    // Answers the probe, and stores every write. With isFirstWriteLost, the answer of the first
    // write is lost, which fails as a connection that broke after the server has read the write.
    private static FakeServer CreateServer(string apiVersion, bool isFirstWriteLost)
    {
        var writes = 0;

        return FakeServer.ForApi(apiVersion, _ =>
        {
            if (isFirstWriteLost && Interlocked.Increment(ref writes) == 1)
            {
                throw new HttpRequestException(HttpRequestError.ResponseEnded, "The write was stored, but its answer was lost.");
            }

            return Stored(apiVersion);
        });
    }

    // Without a custom HttpClient, the client comes from the registration for dependency
    // injection. With one, the HttpClient comes from the HttpClient factory, as an application
    // that passes in its own HttpClient may set it up.
    private static ServiceProvider CreateServiceProvider(string setup, Uri baseUrl, Action<HttpStandardResilienceOptions> configure)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", baseUrl.ToString()),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", ApiToken)
                ]
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        switch (setup)
        {
            case "for-all-clients":
                services.ConfigureHttpClientDefaults(builder => builder.AddStandardResilienceHandler(configure));
                services.AddEventSourcingDb(configuration);
                break;
            case "for-the-client":
                services.AddEventSourcingDb(configuration).AddStandardResilienceHandler(configure);
                break;
            case "custom-http-client":
                services
                    .AddHttpClient("eventsourcingdb", httpClient =>
                    {
                        httpClient.BaseAddress = baseUrl;
                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiToken);
                    })
                    .AddStandardResilienceHandler(configure);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(setup), setup, "Unknown setup.");
        }

        return services.BuildServiceProvider();
    }

    private static IClient CreateClient(string setup, ServiceProvider serviceProvider)
    {
        if (setup != "custom-http-client")
        {
            return serviceProvider.GetRequiredService<IClient>();
        }

        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("eventsourcingdb");

        return new Client(httpClient);
    }

    // Answers the probe, and stores the first attempt of a write but loses its answer, which
    // fails as a connection that broke. Every later attempt is refused.
    private sealed class FirstAttemptLostServer(string apiVersion, bool bufferTheBody) : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
        {
            if (httpRequest.RequestUri!.AbsolutePath == FakeServer.ProbePath)
            {
                return FakeServer.AnswerProbe(apiVersion);
            }

            if (bufferTheBody)
            {
                await httpRequest.Content!.ReadAsByteArrayAsync(cancellationToken);
            }
            else
            {
                await httpRequest.Content!.CopyToAsync(Stream.Null, cancellationToken);
            }

            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new HttpRequestException(HttpRequestError.ResponseEnded, "The write was stored, but its answer was lost.");
            }

            return Refusal(apiVersion);
        }
    }

    // Answers the probe, and breaks the connection while the first attempt of a write sends its
    // body, which the HttpClient reports as an HttpRequestException. A later attempt is stored.
    private sealed class BrokenWhileSendingServer(string apiVersion) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
        {
            if (httpRequest.RequestUri!.AbsolutePath == FakeServer.ProbePath)
            {
                return FakeServer.AnswerProbe(apiVersion);
            }

            try
            {
                await using var connection = new BrokenConnectionStream();
                await httpRequest.Content!.CopyToAsync(connection, cancellationToken);
            }
            catch (IOException exception)
            {
                throw new HttpRequestException(HttpRequestError.Unknown, "Error while copying content to a stream.", exception);
            }

            return Stored(apiVersion);
        }
    }

    private sealed class BrokenConnectionStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException(new IOException("The connection broke while the write was sent."));
        }
    }

    private sealed class RetryOnceHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return await base.SendAsync(request, cancellationToken);
            }
        }
    }

    // Reads the body before passing the request on, as a handler that logs it does.
    private sealed class BufferingHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    // Reads the body as a stream before passing the request on, as a handler that logs it
    // without buffering it does.
    private sealed class StreamReadingHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                await using var stream = await request.Content.ReadAsStreamAsync(cancellationToken);
                await stream.CopyToAsync(Stream.Null, cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
