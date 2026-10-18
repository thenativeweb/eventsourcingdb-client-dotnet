using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class ApiVersionDetectionTests
{
    private const string V1Ping = """{"specversion":"1.0","type":"io.eventsourcingdb.api.ping-received"}""";
    private const string V2Ping = """{"type":"event","data":{"type":"io.eventsourcingdb.api.ping-received"}}""";

    [Fact]
    public void MakesNoRequestWhenTheClientIsCreated()
    {
        var server = new FakeServer(_ => throw new InvalidOperationException("No request expected."));

        _ = server.CreateClient();

        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task UsesTheV2ApiIfTheServerAnswersThePingOfTheV2Api()
    {
        var server = FakeServer.ForApi("v2", _ => FakeServer.Json(HttpStatusCode.OK, V2Ping));
        var client = server.CreateClient();

        await client.PingAsync(TestContext.Current.CancellationToken);

        Assert.Collection(server.Requests,
            request => Assert.Equal(new FakeRequest("GET", "/api/v2/system/ping", null), request),
            request => Assert.Equal(new FakeRequest("GET", "/api/v2/system/ping", null), request)
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task UsesTheV1ApiIfTheServerDoesNotKnowThePingOfTheV2Api(HttpStatusCode status)
    {
        var server = new FakeServer(request => FakeServer.IsProbe(request)
            ? FakeServer.Text(status, "404 page not found\n")
            : FakeServer.Json(HttpStatusCode.OK, V1Ping));
        var client = server.CreateClient();

        await client.PingAsync(TestContext.Current.CancellationToken);

        Assert.Collection(server.Requests,
            request => Assert.Equal(new FakeRequest("GET", "/api/v2/system/ping", null), request),
            request => Assert.Equal(new FakeRequest("GET", "/api/v1/ping", null), request)
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task UsesTheV1ApiIfAnAnswerOf404Or405IsNotFromEventSourcingDb(HttpStatusCode status)
    {
        // Such as a proxy in front of a server before EventSourcingDB 1.3 that passes on only the
        // paths of the v1 API, and answers every other one itself. The requests to the v1 API
        // check that their answers come from EventSourcingDB anyway.
        var server = new FakeServer(request => FakeServer.IsProbe(request)
            ? FakeServer.Respond(status, "<html>Not Found</html>", "text/html", isFromEventSourcingDb: false)
            : FakeServer.Json(HttpStatusCode.OK, V1Ping));
        var client = server.CreateClient();

        await client.PingAsync(TestContext.Current.CancellationToken);
        await client.PingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, server.ProbeCount);
        Assert.Equal(2, server.Requests.Count(request => request.Path == "/api/v1/ping"));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task ProbesOnlyOnceForEveryRequestOfAClient(string apiVersion)
    {
        var ping = apiVersion == "v1" ? V1Ping : V2Ping;
        var server = new FakeServer(request => FakeServer.IsProbe(request)
            ? FakeServer.AnswerProbe(apiVersion)
            : FakeServer.Json(HttpStatusCode.OK, ping));
        var client = server.CreateClient();

        await client.PingAsync(TestContext.Current.CancellationToken);
        await client.PingAsync(TestContext.Current.CancellationToken);
        await client.PingAsync(TestContext.Current.CancellationToken);

        // On v2, the ping goes to the same path as the probe, so every request after the first
        // probe is a ping.
        Assert.Equal(4, server.Requests.Count);
        Assert.Equal(apiVersion == "v1" ? 1 : 4, server.ProbeCount);
        Assert.Equal(apiVersion == "v1" ? 3 : 0, server.Requests.Count(request => request.Path == "/api/v1/ping"));
    }

    [Fact]
    public async Task ProbesForEveryClientThatIsCreatedDirectlyOnItsOwn()
    {
        var server = new FakeServer(request => FakeServer.IsProbe(request)
            ? FakeServer.AnswerProbe("v1")
            : FakeServer.Json(HttpStatusCode.OK, V1Ping));

        await server.CreateClient().PingAsync(TestContext.Current.CancellationToken);
        await server.CreateClient().PingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, server.ProbeCount);
    }

    [Fact]
    public async Task SharesTheProbeBetweenAllClientsOfARegistrationForDependencyInjection()
    {
        var server = new FakeServer(request => FakeServer.IsProbe(request)
            ? FakeServer.AnswerProbe("v1")
            : FakeServer.Json(HttpStatusCode.OK, V1Ping));
        await using var serviceProvider = CreateServiceProvider(server);

        // Like the requests of an application, each of which resolves a client of its own.
        for (var i = 0; i < 5; i++)
        {
            using var scope = serviceProvider.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IClient>();
            await client.PingAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, server.ProbeCount);
        Assert.Equal(5, server.Requests.Count(request => request.Path == "/api/v1/ping"));
    }

    [Fact]
    public async Task DoesNotShareAFailedProbeBetweenTheClientsOfARegistrationForDependencyInjection()
    {
        var probeCount = 0;
        var server = new FakeServer(request =>
        {
            if (!FakeServer.IsProbe(request))
            {
                return FakeServer.Json(HttpStatusCode.OK, V1Ping);
            }

            return Interlocked.Increment(ref probeCount) == 1
                ? FakeServer.Text(HttpStatusCode.ServiceUnavailable, "the server is shutting down\n")
                : FakeServer.AnswerProbe("v1");
        });
        await using var serviceProvider = CreateServiceProvider(server);

        using (var scope = serviceProvider.CreateScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<IClient>();
            await Assert.ThrowsAsync<DbApiException>(() => client.PingAsync(TestContext.Current.CancellationToken));
        }

        for (var i = 0; i < 2; i++)
        {
            using var scope = serviceProvider.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IClient>();
            await client.PingAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, server.ProbeCount);
    }

    [Fact]
    public async Task ProbesForEveryRegistrationForDependencyInjectionOnItsOwn()
    {
        var server = new FakeServer(request => FakeServer.IsProbe(request)
            ? FakeServer.AnswerProbe("v1")
            : FakeServer.Json(HttpStatusCode.OK, V1Ping));
        await using var firstServiceProvider = CreateServiceProvider(server);
        await using var secondServiceProvider = CreateServiceProvider(server);

        await firstServiceProvider.GetRequiredService<IClient>().PingAsync(TestContext.Current.CancellationToken);
        await secondServiceProvider.GetRequiredService<IClient>().PingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, server.ProbeCount);
    }

    public static TheoryData<string> FailedProbes =>
    [
        "a status of 500",
        "a status of 503",
        "a status of 429",
        "an answer of 200 that is not from EventSourcingDB",
        "an answer of 500 that is not from EventSourcingDB",
        "an answer of 200 that is no envelope of the v2 API",
        "an answer of 200 that is no JSON",
        "a network error",
    ];

    [Theory]
    [MemberData(nameof(FailedProbes))]
    public async Task FailsTheFirstRequestWithTheErrorOfAFailedProbeAndProbesAgainOnTheNextOne(string failure)
    {
        var (answerFirstProbe, expectedExceptionType) = GetFailedProbe(failure);
        var probeCount = 0;
        var server = new FakeServer(request =>
        {
            if (!FakeServer.IsProbe(request))
            {
                return FakeServer.Json(HttpStatusCode.OK, V1Ping);
            }

            probeCount++;
            return probeCount == 1 ? answerFirstProbe() : FakeServer.AnswerProbe("v1");
        });
        var client = server.CreateClient();

        var exception = await Record.ExceptionAsync(() => client.PingAsync(TestContext.Current.CancellationToken));

        Assert.IsType(expectedExceptionType, exception);
        Assert.Single(server.Requests);

        await client.PingAsync(TestContext.Current.CancellationToken);
        await client.PingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, server.ProbeCount);
        Assert.Equal(2, server.Requests.Count(request => request.Path == "/api/v1/ping"));
    }

    [Fact]
    public async Task FailsWithTheStatusAndTheTextOfAProbeThatTheServerRefuses()
    {
        var server = new FakeServer(_ => FakeServer.Text(HttpStatusCode.InternalServerError, "internal server error\n"));
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(() => client.PingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.Status);
        Assert.Equal("internal server error", exception.Description);
        Assert.Null(exception.Code);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task SharesOneProbeBetweenConcurrentFirstRequests(string apiVersion)
    {
        var ping = apiVersion == "v1" ? V1Ping : V2Ping;
        var probeArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answerProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCount = 0;

        var server = new FakeServer(async (request, token) =>
        {
            if (FakeServer.IsProbe(request) && Interlocked.Increment(ref probeCount) == 1)
            {
                probeArrived.TrySetResult();
                await answerProbe.Task.WaitAsync(token);

                return FakeServer.AnswerProbe(apiVersion);
            }

            return FakeServer.Json(HttpStatusCode.OK, ping);
        });
        var client = server.CreateClient();

        var first = client.PingAsync(TestContext.Current.CancellationToken);
        await probeArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var second = client.PingAsync(TestContext.Current.CancellationToken);
        var third = client.PingAsync(TestContext.Current.CancellationToken);

        answerProbe.SetResult();
        await Task.WhenAll(first, second, third);

        // One probe and three pings. On v2, the pings go to the path of the probe as well, so the
        // requests are counted as a whole: with a probe for each ping, there would be six.
        Assert.Equal(4, server.Requests.Count);
    }

    [Fact]
    public async Task FailsEveryRequestThatWaitsForAProbeThatFails()
    {
        var probeArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answerProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCount = 0;

        var server = new FakeServer(async (request, token) =>
        {
            if (!FakeServer.IsProbe(request))
            {
                return FakeServer.Json(HttpStatusCode.OK, V1Ping);
            }

            if (Interlocked.Increment(ref probeCount) == 1)
            {
                probeArrived.TrySetResult();
                await answerProbe.Task.WaitAsync(token);

                return FakeServer.Text(HttpStatusCode.ServiceUnavailable, "the server is shutting down\n");
            }

            return FakeServer.AnswerProbe("v1");
        });
        var client = server.CreateClient();

        var first = client.PingAsync(TestContext.Current.CancellationToken);
        await probeArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var second = client.PingAsync(TestContext.Current.CancellationToken);

        answerProbe.SetResult();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Assert.ThrowsAsync<DbApiException>(() => first)).Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Assert.ThrowsAsync<DbApiException>(() => second)).Status);
        Assert.Equal(1, Volatile.Read(ref probeCount));

        await client.PingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref probeCount));
    }

    [Fact]
    public async Task LetsACallerStopWaitingForTheProbeWithoutFailingTheOthers()
    {
        var probeArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answerProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var server = new FakeServer(async (request, token) =>
        {
            if (FakeServer.IsProbe(request))
            {
                probeArrived.TrySetResult();
                await answerProbe.Task.WaitAsync(token);

                return FakeServer.AnswerProbe("v1");
            }

            return FakeServer.Json(HttpStatusCode.OK, V1Ping);
        });
        var client = server.CreateClient();

        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cancelled = client.PingAsync(source.Token);
        await probeArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var waiting = client.PingAsync(TestContext.Current.CancellationToken);

        await source.CancelAsync();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(source.Token, exception.CancellationToken);

        answerProbe.SetResult();
        await waiting;

        Assert.Equal(1, server.ProbeCount);
        Assert.Single(server.Requests, request => request.Path == "/api/v1/ping");
    }

    private static (Func<HttpResponseMessage> Answer, Type ExpectedExceptionType) GetFailedProbe(string failure)
    {
        return failure switch
        {
            "a status of 500" => (() => FakeServer.Text(HttpStatusCode.InternalServerError, "internal server error\n"), typeof(DbApiException)),
            "a status of 503" => (() => FakeServer.Text(HttpStatusCode.ServiceUnavailable, "the server is shutting down\n"), typeof(DbApiException)),
            "a status of 429" => (() => FakeServer.Text(HttpStatusCode.TooManyRequests, "too many requests\n"), typeof(DbApiException)),
            "an answer of 200 that is not from EventSourcingDB" => (() => FakeServer.Respond(HttpStatusCode.OK, V2Ping, "application/json", isFromEventSourcingDb: false), typeof(InvalidOperationException)),
            "an answer of 500 that is not from EventSourcingDB" => (() => FakeServer.Respond(HttpStatusCode.InternalServerError, "bad gateway", "text/plain", isFromEventSourcingDb: false), typeof(InvalidOperationException)),
            "an answer of 200 that is no envelope of the v2 API" => (() => FakeServer.Json(HttpStatusCode.OK, V1Ping), typeof(InvalidValueException)),
            "an answer of 200 that is no JSON" => (() => FakeServer.Text(HttpStatusCode.OK, "<html></html>"), typeof(InvalidValueException)),
            "a network error" => (() => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"), typeof(HttpRequestException)),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown failure."),
        };
    }

    private static ServiceProvider CreateServiceProvider(FakeServer server)
    {
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
        services.AddEventSourcingDb(configuration).ConfigurePrimaryHttpMessageHandler(() => server);

        return services.BuildServiceProvider();
    }
}
