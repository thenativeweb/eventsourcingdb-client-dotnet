using System;
using System.Net.Http;
using System.Threading.Tasks;

using Xunit;

namespace EventSourcingDb.Tests;

public sealed class PingTests : EventSourcingDbTests
{
    [Fact]
    public async Task DoesNotThrowIfServerIsReachable()
    {
        var client = Container!.GetClient();

        // Should not throw.
        await client.PingAsync();
    }

    [Fact]
    public async Task ThrowsIfServerIsNotReachable()
    {
        var port = Container!.GetMappedPort();
        var apiToken = Container.GetApiToken();

        var invalidUri = new Uri($"http://non-existent-host:{port}/");

        var httpClient = HttpClientFactory.GetConfiguredDefaultClient(invalidUri, apiToken);

        var client = new Client(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.PingAsync();
        });
    }
}
