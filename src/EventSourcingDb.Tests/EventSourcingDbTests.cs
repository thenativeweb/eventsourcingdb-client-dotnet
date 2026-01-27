using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EventSourcingDb.Tests;

public class EventSourcingDbTests : IAsyncLifetime
{
    protected Container? Container { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var imageVersion = DockerfileHelper.GetImageVersionFromDockerfile();
        Container = new Container().WithImageTag(imageVersion);
        await Container.StartAsync();
    }

    protected ILogger<Client> GetClientLogger(ITestOutputHelper testOutputHelper)
    {
        return new XUnitLogger<Client>(testOutputHelper);
    }

    public async ValueTask DisposeAsync()
    {
        if (Container is not null)
        {
            await Container.StopAsync();
        }
    }
}
