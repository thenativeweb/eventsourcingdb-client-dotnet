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
        await client.PingAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThrowsIfServerIsNotReachable()
    {
        var port = Container!.GetMappedPort();
        var apiToken = Container.GetApiToken();

        var invalidUri = new Uri($"http://non-existent-host:{port}/");
        var client = new Client(invalidUri, apiToken);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.PingAsync(TestContext.Current.CancellationToken);
        });
    }
}
