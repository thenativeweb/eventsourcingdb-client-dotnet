using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

// Proves that the client talks to the container through the API that the container offers.
public sealed class ApiChoiceTests : EventSourcingDbTests
{
    [Fact]
    public async Task UsesTheApiThatTheServerOffers()
    {
        var offersV2 = await OffersTheV2ApiAsync();

        var recorder = new RecordingHandler();
        using var httpClient = new HttpClient(recorder) { BaseAddress = Container!.GetBaseUrl() };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Container.GetApiToken());
        var client = Container.GetClient(httpClient);

        await client.WriteEventsAsync(
            [new EventCandidate("https://www.eventsourcingdb.io", "/test", "io.eventsourcingdb.test", new { value = 23 })],
            token: TestContext.Current.CancellationToken
        );
        await client
            .ReadEventsAsync("/test", new ReadEventsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        string[] expectedRequests = offersV2
            ?
            [
                "GET /api/v2/system/ping",
                "POST /api/v2/data/write-events",
                "QUERY /api/v2/data/read-events",
            ]
            :
            [
                "GET /api/v2/system/ping",
                "POST /api/v1/write-events",
                "POST /api/v1/read-events",
            ];

        Assert.Equal(expectedRequests, recorder.Requests.ToArray());
    }

    private sealed class RecordingHandler() : DelegatingHandler(new SocketsHttpHandler())
    {
        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue($"{request.Method} {request.RequestUri!.AbsolutePath}");

            return base.SendAsync(request, cancellationToken);
        }
    }
}
