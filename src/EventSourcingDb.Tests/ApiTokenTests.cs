using System;
using System.Collections.Generic;
using System.Linq;
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

// The API token goes with every request except the pings, which EventSourcingDB answers without
// one, both for a client created with a URL and an API token, and for one from the registration
// for dependency injection.
public sealed class ApiTokenTests
{
    private const string ApiToken = "secret";

    private static readonly string[] _setups = ["constructor", "dependency-injection"];
    private static readonly string[] _apiVersions = ["v1", "v2"];

    public static TheoryData<string, string> Setups
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var setup in _setups)
            {
                foreach (var apiVersion in _apiVersions)
                {
                    data.Add(setup, apiVersion);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string, string> RequestsExceptThePing
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var setup in _setups)
            {
                foreach (var apiVersion in _apiVersions)
                {
                    foreach (var request in TestRequests.All.Where(request => request != "ping"))
                    {
                        data.Add(setup, apiVersion, request);
                    }
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string> AllRequests
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var apiVersion in _apiVersions)
            {
                foreach (var request in TestRequests.All)
                {
                    data.Add(apiVersion, request);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string, bool> AllRequestsWithAndWithoutDefaults
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach (var apiVersion in _apiVersions)
            {
                foreach (var request in TestRequests.All)
                {
                    data.Add(apiVersion, request, true);
                    data.Add(apiVersion, request, false);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task SendsNoApiTokenWithTheProbeOfTheApiVersionAndThePing(string setup, string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(HttpStatusCode.BadRequest, "refused\n"));
        await using var listener = new FakeServerListener(server);
        await using var serviceProvider = CreateServiceProvider(setup, listener.BaseUrl);
        var client = CreateClient(serviceProvider, listener.BaseUrl);

        var exception = await Record.ExceptionAsync(() => client.PingAsync(TestContext.Current.CancellationToken));

        Assert.IsType<DbApiException>(exception);
        Assert.Collection(server.Requests,
            probe => Assert.Equal(new FakeRequest("GET", FakeServer.ProbePath, null, null), probe),
            ping => Assert.Equal(new FakeRequest("GET", apiVersion == "v1" ? "/api/v1/ping" : "/api/v2/system/ping", null, null), ping)
        );
    }

    [Theory]
    [MemberData(nameof(RequestsExceptThePing))]
    public async Task SendsTheApiTokenWithEveryOtherRequest(string setup, string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(HttpStatusCode.BadRequest, "refused\n"));
        await using var listener = new FakeServerListener(server);
        await using var serviceProvider = CreateServiceProvider(setup, listener.BaseUrl);
        var client = CreateClient(serviceProvider, listener.BaseUrl);

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.IsType<DbApiException>(exception);
        Assert.Collection(server.Requests,
            probe => Assert.Null(probe.Authorization),
            sent => Assert.Equal($"Bearer {ApiToken}", sent.Authorization)
        );
    }

    // A handler that sends a request again, such as the standard resilience handler, sends it
    // with the same headers, so every attempt carries the API token exactly once, a write that it
    // sends again included, and no attempt of the probe or of the ping carries it. The error is
    // the refusal, and for a write, which may have been stored once it was sent twice, an
    // OutcomeUnknownException that holds it.
    [Theory]
    [MemberData(nameof(AllRequestsWithAndWithoutDefaults))]
    public async Task SendsTheApiTokenWithEveryAttemptOfTheStandardResilienceHandler(string apiVersion, string request, bool isForAllClients)
    {
        // The client sends the probe and the request one after the other, so every attempt with
        // an odd number is the first one of either, and fails as a connection that broke. The
        // second attempt of the probe gets its answer, and the second one of the request a refusal.
        var attempts = 0;
        var server = new FakeServer(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt % 2 == 1)
            {
                throw new HttpRequestException(HttpRequestError.ResponseEnded, "The answer was lost.");
            }

            return attempt == 2
                ? FakeServer.AnswerProbe(apiVersion)
                : FakeServer.Text(HttpStatusCode.BadRequest, "refused\n");
        });

        static void Configure(HttpStandardResilienceOptions options)
        {
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.UseJitter = false;
        }

        var services = new ServiceCollection();
        services.AddLogging();

        if (isForAllClients)
        {
            services.ConfigureHttpClientDefaults(builder => builder.AddStandardResilienceHandler(Configure));
            services.AddEventSourcingDb(CreateConfiguration(new Uri("http://esdb.test")))
                .ConfigurePrimaryHttpMessageHandler(() => server);
        }
        else
        {
            services.AddEventSourcingDb(CreateConfiguration(new Uri("http://esdb.test")))
                .ConfigurePrimaryHttpMessageHandler(() => server)
                .AddStandardResilienceHandler(Configure);
        }

        await using var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<IClient>();

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        if (Array.IndexOf(TestRequests.Writes, request) < 0)
        {
            Assert.IsType<DbApiException>(exception);
        }
        else
        {
            var outcomeUnknownException = Assert.IsType<OutcomeUnknownException>(exception);
            Assert.IsType<DbApiException>(outcomeUnknownException.InnerException);
        }

        var expectedAuthorization = request == "ping" ? null : $"Bearer {ApiToken}";
        Assert.Collection(server.Requests,
            probe => Assert.Equal(new FakeRequest("GET", FakeServer.ProbePath, null, null), probe),
            probe => Assert.Equal(new FakeRequest("GET", FakeServer.ProbePath, null, null), probe),
            sent => Assert.Equal(expectedAuthorization, sent.Authorization),
            sent => Assert.Equal(expectedAuthorization, sent.Authorization)
        );
    }

    // An HttpClient that the application passes in brings its own Authorization header, which
    // the client leaves alone. A default header cannot be left out of a single request, so it
    // goes with the pings as well.
    [Theory]
    [MemberData(nameof(AllRequests))]
    public async Task KeepsTheAuthorizationHeaderOfACustomHttpClient(string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(HttpStatusCode.BadRequest, "refused\n"));
        using var httpClient = new HttpClient(server) { BaseAddress = new Uri("http://esdb.test") };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "custom");
        var client = new Client(httpClient);

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.IsType<DbApiException>(exception);
        Assert.Collection(server.Requests,
            probe => Assert.Equal("Bearer custom", probe.Authorization),
            sent => Assert.Equal("Bearer custom", sent.Authorization)
        );
    }

    // Without a service provider, the client is created with a URL and an API token.
    private static ServiceProvider? CreateServiceProvider(string setup, Uri baseUrl)
    {
        if (setup == "constructor")
        {
            return null;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventSourcingDb(CreateConfiguration(baseUrl));

        return services.BuildServiceProvider();
    }

    private static IConfiguration CreateConfiguration(Uri baseUrl)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", baseUrl.ToString()),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", ApiToken)
                ]
            )
            .Build();
    }

    private static IClient CreateClient(ServiceProvider? serviceProvider, Uri baseUrl)
    {
        return serviceProvider is null
            ? new Client(baseUrl, ApiToken)
            : serviceProvider.GetRequiredService<IClient>();
    }
}
