using System;
using System.Net.Http;
using System.Threading.Tasks;

using Xunit;

namespace EventSourcingDb.Tests;

public sealed class PingTests : IAsyncLifetime
{
    private Container? _container;

    public async Task InitializeAsync()
    {
        var imageVersion = DockerfileHelper.GetImageVersionFromDockerfile();
        _container = new Container().WithImageTag(imageVersion);
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
    }

    [Fact]
    public async Task DoesNotThrowIfServerIsReachable()
    {
        var client = _container!.GetClient();

        // Should not throw.
        await client.PingAsync();
    }

    [Fact]
    public async Task ThrowsIfServerIsNotReachable()
    {
        var port = _container!.GetMappedPort();
        var apiToken = _container.GetApiToken();

        var invalidUri = new Uri($"http://non-existent-host:{port}/");
        var client = new Client(invalidUri, apiToken);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.PingAsync();
        });
    }
}
