using System.Net.Http;
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
        await client.VerifyApiTokenAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThrowsIfTheTokenIsInvalid()
    {
        var url = Container!.GetBaseUrl();
        var invalidApiToken = $"{Container.GetApiToken()}-invalid";

        var client = new Client(url, invalidApiToken);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.VerifyApiTokenAsync(TestContext.Current.CancellationToken);
        });
    }
}
