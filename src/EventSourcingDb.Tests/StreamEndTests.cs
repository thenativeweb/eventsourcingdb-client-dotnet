using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

// Every stream of the v2 API that is finite ends with an end item. One that breaks off before it
// was aborted by a failure, so it fails, too.
public sealed class StreamEndTests
{
    public static TheoryData<string> FiniteStreams => ["read-events", "read-subjects", "read-event-types", "run-eventql-query"];

    [Theory]
    [MemberData(nameof(FiniteStreams))]
    public async Task FailsIfAFiniteStreamEndsWithoutItsEndItem(string request)
    {
        var server = FakeServer.ForApi("v2", _ => FakeServer.Ndjson(
            TestRequests.FirstItemOf("v2", request),
            TestRequests.Heartbeat("v2")
        ));
        var client = server.CreateClient();
        var itemCount = 0;

        var exception = await Assert.ThrowsAsync<HttpIOException>(async () =>
        {
            await foreach (var _ in TestRequests.GetStream(client, request, TestContext.Current.CancellationToken))
            {
                itemCount++;
            }
        });

        Assert.Equal(1, itemCount);
        Assert.Equal(HttpRequestError.ResponseEnded, exception.HttpRequestError);
    }

    [Theory]
    [MemberData(nameof(FiniteStreams))]
    public async Task EndsAFiniteStreamWithItsEndItem(string request)
    {
        // Nothing after the end item is read.
        var server = FakeServer.ForApi("v2", _ => FakeServer.Ndjson(
            TestRequests.FirstItemOf("v2", request),
            TestRequests.End,
            "not an item"
        ));
        var client = server.CreateClient();
        var itemCount = 0;

        await foreach (var _ in TestRequests.GetStream(client, request, TestContext.Current.CancellationToken))
        {
            itemCount++;
        }

        Assert.Equal(1, itemCount);
    }

    [Fact]
    public async Task FailsIfAStreamWithoutItemsEndsWithoutItsEndItem()
    {
        var server = FakeServer.ForApi("v2", _ => FakeServer.Ndjson());
        var client = server.CreateClient();

        await Assert.ThrowsAsync<HttpIOException>(async () =>
        {
            await foreach (var _ in client.ReadEventTypesAsync(TestContext.Current.CancellationToken))
            {
            }
        });
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task FailsTheObservationIfTheServerClosesItsStreamWithoutAnError(string apiVersion)
    {
        // Observing never ends on its own, so a stream that closes was aborted by a failure.
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Ndjson(
            TestRequests.Event(apiVersion, "0"),
            TestRequests.Heartbeat(apiVersion)
        ));
        var client = server.CreateClient();
        var itemCount = 0;

        var exception = await Assert.ThrowsAsync<HttpIOException>(async () =>
        {
            await foreach (var _ in TestRequests.GetStream(client, "observe-events", TestContext.Current.CancellationToken))
            {
                itemCount++;
            }
        });

        Assert.Equal(1, itemCount);
        Assert.Equal(HttpRequestError.ResponseEnded, exception.HttpRequestError);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task FailsTheObservationIfTheServerClosesItsStreamBeforeAnyItem(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Ndjson());
        var client = server.CreateClient();

        await Assert.ThrowsAsync<HttpIOException>(async () =>
        {
            await foreach (var _ in TestRequests.GetStream(client, "observe-events", TestContext.Current.CancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task EndsAStreamOfTheV1ApiWithoutAnEndItem()
    {
        // The streams of the v1 API have no end item.
        var server = FakeServer.ForApi("v1", _ => FakeServer.Ndjson(TestRequests.Event("v1", "0")));
        var client = server.CreateClient();
        var itemCount = 0;

        await foreach (var _ in TestRequests.GetStream(client, "read-events", TestContext.Current.CancellationToken))
        {
            itemCount++;
        }

        Assert.Equal(1, itemCount);
    }
}
