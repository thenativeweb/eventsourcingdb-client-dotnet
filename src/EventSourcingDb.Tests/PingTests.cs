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
        this._container = new Container().WithImageTag(imageVersion);
        await this._container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (this._container is not null)
        {
            await this._container.StopAsync();
        }
    }

    [Fact]
    public async Task DoesNotThrowIfServerIsReachable()
    {
        var client = this._container!.GetClient();

        // Should not throw.
        await client.PingAsync();
    }

    [Fact]
    public async Task ThrowsIfServerIsNotReachable()
    {
        var port = this._container!.GetMappedPort();
        var apiToken = this._container.GetApiToken();

        var invalidUri = new Uri($"http://non-existent-host:{port}/");
        var client = new Client(invalidUri, apiToken);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.PingAsync();
        });

        Assert.Contains("No such host", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
