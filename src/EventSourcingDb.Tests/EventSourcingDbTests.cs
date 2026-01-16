using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingDb.Tests;

public class EventSourcingDbTests : IAsyncLifetime
{
    protected Container? Container { get; private set; }

    public async Task InitializeAsync()
    {
        var imageVersion = DockerfileHelper.GetImageVersionFromDockerfile();
        Container = new Container().WithImageTag(imageVersion);
        await Container.StartAsync();
    }

    public ILogger<Client> GetClientLogger(ITestOutputHelper testOutputHelper)
    {
        return new XUnitLogger<Client>(testOutputHelper);
    }

    public async Task DisposeAsync()
    {
        if (Container is not null)
        {
            await Container.StopAsync();
        }
    }
}
