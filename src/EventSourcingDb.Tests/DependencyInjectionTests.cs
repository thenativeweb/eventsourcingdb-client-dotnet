using System.Collections.Generic;
using System.Threading.Tasks;
using EventSourcingDb.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class DependencyInjectionTests : IAsyncLifetime
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
    public async Task ClientIsAvailableUsingDependencyInjection()
    {
        var url = _container!.GetBaseUrl();
        var apiToken = _container!.GetApiToken();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("EventSourcingDb:BaseUrl", url.ToString()),
                    new KeyValuePair<string, string?>("EventSourcingDb:ApiToken", apiToken)
                ]
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventSourcingDb(configuration);

        var serviceProvider = services.BuildServiceProvider();

        var client = serviceProvider.GetService<IClient>();

        await client!.PingAsync();
    }
}
