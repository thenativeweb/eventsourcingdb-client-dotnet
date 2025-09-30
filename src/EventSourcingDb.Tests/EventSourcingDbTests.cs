using System.Threading.Tasks;
using Xunit;

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

    public async Task DisposeAsync()
    {
        if (Container is not null)
        {
            await Container.StopAsync();
        }
    }
}
