using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace EventSourcingDb.Tests;

// A handler that sends a write again, such as a retry policy, can have the first attempt stored,
// lose its answer, and get a refusal for the second, for example because the precondition no
// longer holds after the first. So once a write may have reached the server more than once, no
// refusal tells that it was not stored.
public sealed class RetriedWriteTests
{
    private static readonly string[] _apiVersions = ["v1", "v2"];

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

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteThatAHandlerSentAgainAsOfUnknownOutcomeAlthoughTheServerRefusesTheRetry(string apiVersion, string request)
    {
        var server = new FirstAttemptLostServer(apiVersion, bufferTheBody: false);
        var client = new Client(new HttpClient(new RetryOnceHandler(server)) { BaseAddress = new Uri("http://esdb.test") });

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.Equal(2, server.Attempts);
        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.IsType<DbApiException>(outcomeUnknownException.InnerException);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteThatAHandlerSentAgainFromABufferAsOfUnknownOutcomeAlthoughTheServerRefusesTheRetry(string apiVersion, string request)
    {
        // Reading the body into a buffer lets every later attempt copy it from there, so the
        // attempts cannot be counted any more.
        var server = new FirstAttemptLostServer(apiVersion, bufferTheBody: true);
        var client = new Client(new HttpClient(new RetryOnceHandler(server)) { BaseAddress = new Uri("http://esdb.test") });

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.Equal(2, server.Attempts);
        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.IsType<DbApiException>(outcomeUnknownException.InnerException);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task ReportsAWriteThatTheStandardResilienceHandlerSentAgainAsOfUnknownOutcome(string apiVersion, string request)
    {
        // As the service defaults of .NET Aspire add to every HttpClient.
        var server = new FirstAttemptLostServer(apiVersion, bufferTheBody: false);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", "http://esdb.test"),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", "secret")
                ]
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(builder => builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.UseJitter = false;
        }));
        services.AddEventSourcingDb(configuration).ConfigurePrimaryHttpMessageHandler(() => server);

        await using var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<IClient>();

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.Equal(2, server.Attempts);
        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.IsType<DbApiException>(outcomeUnknownException.InnerException);
    }

    public static TheoryData<string, string, bool> WritesWithAndWithoutDefaults
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach (var apiVersion in _apiVersions)
            {
                foreach (var request in TestRequests.Writes)
                {
                    data.Add(apiVersion, request, true);
                    data.Add(apiVersion, request, false);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(WritesWithAndWithoutDefaults))]
    public async Task SendsAWriteOnlyOnceIfTheStandardResilienceHandlerIsConfiguredAsTheReadmeSays(string apiVersion, string request, bool isForAllClients)
    {
        var server = new FirstAttemptLostServer(apiVersion, bufferTheBody: false);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", "http://esdb.test"),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", "secret")
                ]
            )
            .Build();

        static void Configure(HttpStandardResilienceOptions options)
        {
            options.Retry.DisableForUnsafeHttpMethods();
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.UseJitter = false;
        }

        var services = new ServiceCollection();
        services.AddLogging();

        if (isForAllClients)
        {
            services.ConfigureHttpClientDefaults(builder => builder.AddStandardResilienceHandler(Configure));
            services.AddEventSourcingDb(configuration).ConfigurePrimaryHttpMessageHandler(() => server);
        }
        else
        {
            services.AddEventSourcingDb(configuration)
                .ConfigurePrimaryHttpMessageHandler(() => server)
                .AddStandardResilienceHandler(Configure);
        }

        await using var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<IClient>();

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.Equal(1, server.Attempts);
        var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
        Assert.IsType<HttpRequestException>(outcomeUnknownException.InnerException);
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
                await httpRequest.Content!.CopyToAsync(System.IO.Stream.Null, cancellationToken);
            }

            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new HttpRequestException(HttpRequestError.ResponseEnded, "The write was stored, but its answer was lost.");
            }

            return Refusal(apiVersion);
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
}
