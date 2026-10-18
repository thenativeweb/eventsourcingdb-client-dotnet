using System;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public class ContainerTests : IAsyncDisposable
{
    private Container? _container;

    [Fact]
    public async Task StartsWithACustomPort()
    {
        var imageTag = DockerfileHelper.GetImageTag();

        _container = new Container()
            .WithImageTag(imageTag)
            .WithPort(4000);

        await _container.StartAsync(TestContext.Current.CancellationToken);

        var client = _container.GetClient();

        // Should not throw.
        await client.PingAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
    }
}
