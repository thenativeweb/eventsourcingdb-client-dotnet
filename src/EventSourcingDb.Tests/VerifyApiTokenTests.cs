using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public class VerifyApiTokenTests : IAsyncLifetime
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
    public async Task DoesNotThrowIfTheTokenIsValid()
    {
        var client = _container!.GetClient();

        // Should not throw.
        await client.VerifyApiTokenAsync();
    }

    [Fact]
    public async Task ThrowsIfTheTokenIsInvalid()
    {
        var url = _container!.GetBaseUrl();
        var invalidApiToken = $"{_container.GetApiToken()}-invalid";

        var client = new Client(url, invalidApiToken);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.VerifyApiTokenAsync();
        });
    }
}
