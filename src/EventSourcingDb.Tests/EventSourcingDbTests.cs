using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EventSourcingDb.Tests;

public class EventSourcingDbTests : IAsyncLifetime
{
    protected Container? Container { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var imageTag = DockerfileHelper.GetImageTag();
        Container = new Container().WithImageTag(imageTag);
        await Container.StartAsync();
    }

    protected ILogger<Client> GetClientLogger(ITestOutputHelper testOutputHelper)
    {
        return new XUnitLogger<Client>(testOutputHelper);
    }

    // A server offers the v2 API if it answers its ping with 200. Tests find that out on their
    // own, rather than from the tag of the image, since the tag tells nothing about the API once a
    // release that offers v2 is the latest one.
    protected async Task<bool> OffersTheV2ApiAsync()
    {
        using var httpClient = new HttpClient { BaseAddress = Container!.GetBaseUrl() };
        using var response = await httpClient.GetAsync("/api/v2/system/ping", TestContext.Current.CancellationToken);

        return response.StatusCode is HttpStatusCode.OK;
    }

    public async ValueTask DisposeAsync()
    {
        if (Container is not null)
        {
            await Container.StopAsync();
        }
    }
}
