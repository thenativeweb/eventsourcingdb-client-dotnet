using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class CustomHttpClientTests : EventSourcingDbTests
{
    [Fact]
    public async Task UsesCustomHttpClientSuccessfully()
    {
        var baseUrl = Container!.GetBaseUrl();
        var apiToken = Container.GetApiToken();

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = baseUrl
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        var client = new Client(httpClient);

        await client.PingAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CustomHttpClientWithCustomHeadersWorks()
    {
        var baseUrl = Container!.GetBaseUrl();
        var apiToken = Container.GetApiToken();

        var httpClient = new HttpClient
        {
            BaseAddress = baseUrl
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        httpClient.DefaultRequestHeaders.Add("X-Custom-Header", "test-value");

        var client = new Client(httpClient);

        await client.PingAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ContainerCanProvideClientWithCustomHttpClient()
    {
        var baseUrl = Container!.GetBaseUrl();
        var apiToken = Container.GetApiToken();

        var httpClient = new HttpClient
        {
            BaseAddress = baseUrl,
            Timeout = TimeSpan.FromSeconds(30)
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        var client = Container.GetClient(httpClient);

        await client.PingAsync(TestContext.Current.CancellationToken);
    }
}
