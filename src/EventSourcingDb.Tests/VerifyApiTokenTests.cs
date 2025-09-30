using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public class VerifyApiTokenTests : EventSourcingDbTests
{
    [Fact]
    public async Task DoesNotThrowIfTheTokenIsValid()
    {
        var client = Container!.GetClient();

        // Should not throw.
        await client.VerifyApiTokenAsync();
    }

    [Fact]
    public async Task ThrowsIfTheTokenIsInvalid()
    {
        var url = Container!.GetBaseUrl();
        var invalidApiToken = $"{Container!.GetApiToken()}-invalid";

        var httpClient = HttpClientFactory.GetConfiguredDefaultClient(url, invalidApiToken);

        var client = new Client(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.VerifyApiTokenAsync();
        });
    }
}
