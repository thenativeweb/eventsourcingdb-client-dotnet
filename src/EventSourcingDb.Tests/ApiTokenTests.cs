using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using EventSourcingDb.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddEventSourcingDb(configuration);

        return services.BuildServiceProvider();
    }

    private static IClient CreateClient(ServiceProvider? serviceProvider, Uri baseUrl)
    {
        return serviceProvider is null
            ? new Client(baseUrl, ApiToken)
            : serviceProvider.GetRequiredService<IClient>();
    }
}
