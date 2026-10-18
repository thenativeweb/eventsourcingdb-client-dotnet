using System.Net;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

// Each request goes to the route of the API that the server offers, with its method and its body.
public sealed class RequestTests
{
    private const string WriteEventsBody = """{"events":[{"source":"https://www.eventsourcingdb.io","subject":"/test","type":"io.eventsourcingdb.test","data":{"value":23}}],"preconditions":[]}""";

    public static TheoryData<string, string, string, string, string?> Requests => new()
    {
        { "v1", "ping", "GET", "/api/v1/ping", null },
        { "v1", "verify-api-token", "POST", "/api/v1/verify-api-token", null },
        { "v1", "write-events", "POST", "/api/v1/write-events", WriteEventsBody },
        { "v1", "register-event-schema", "POST", "/api/v1/register-event-schema", """{"eventType":"io.eventsourcingdb.test","schema":{"type":"object"}}""" },
        { "v1", "read-event-type", "POST", "/api/v1/read-event-type", """{"eventType":"io.eventsourcingdb.test"}""" },
        { "v1", "read-events", "POST", "/api/v1/read-events", """{"subject":"/","options":{"recursive":true}}""" },
        { "v1", "read-subjects", "POST", "/api/v1/read-subjects", """{"baseSubject":"/"}""" },
        { "v1", "read-event-types", "POST", "/api/v1/read-event-types", null },
        { "v1", "observe-events", "POST", "/api/v1/observe-events", """{"subject":"/","options":{"recursive":true}}""" },
        { "v1", "run-eventql-query", "POST", "/api/v1/run-eventql-query", """{"query":"FROM e IN events PROJECT INTO e"}""" },
        { "v2", "ping", "GET", "/api/v2/system/ping", null },
        { "v2", "verify-api-token", "POST", "/api/v2/system/verify-token", null },
        { "v2", "write-events", "POST", "/api/v2/data/write-events", WriteEventsBody },
        { "v2", "register-event-schema", "POST", "/api/v2/data/register-event-schema", """{"type":"io.eventsourcingdb.test","schema":{"type":"object"}}""" },
        { "v2", "read-event-type", "QUERY", "/api/v2/data/read-event-type", """{"type":"io.eventsourcingdb.test"}""" },
        { "v2", "read-events", "QUERY", "/api/v2/data/read-events", """{"subject":"/","options":{"recursive":true}}""" },
        { "v2", "read-subjects", "QUERY", "/api/v2/data/read-subjects", """{"subject":"/","options":{"recursive":true}}""" },
        { "v2", "read-event-types", "QUERY", "/api/v2/data/read-event-types", null },
        { "v2", "observe-events", "QUERY", "/api/v2/data/observe-events", """{"subject":"/","options":{"recursive":true}}""" },
        { "v2", "run-eventql-query", "QUERY", "/api/v2/data/run-eventql-query", """{"query":"FROM e IN events PROJECT INTO e"}""" },
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task SendsEachRequestToTheRouteOfTheApiThatTheServerOffers(
        string apiVersion,
        string request,
        string expectedMethod,
        string expectedPath,
        string? expectedBody)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(HttpStatusCode.BadRequest, "refused\n"));
        var client = server.CreateClient();

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.IsType<DbApiException>(exception);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(new FakeRequest(expectedMethod, expectedPath, expectedBody), server.Requests[1]);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task LeavesOutOptionsThatAreNotSet(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(HttpStatusCode.BadRequest, "refused\n"));
        var client = server.CreateClient();

        await Assert.ThrowsAsync<DbApiException>(async () =>
        {
            await foreach (var _ in client.ReadEventsAsync("/test", new ReadEventsOptions(Recursive: false, Order: Order.Antichronological), TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal("""{"subject":"/test","options":{"recursive":false,"order":"antichronological"}}""", server.Requests[1].Body);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task SendsAllOptionsThatAreSet(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(HttpStatusCode.BadRequest, "refused\n"));
        var client = server.CreateClient();

        var options = new ReadEventsOptions(
            Recursive: true,
            Order: Order.Chronological,
            UpperBound: new Bound("10", BoundType.Exclusive),
            FromLatestEvent: new ReadFromLatestEvent("/test", "io.eventsourcingdb.test", ReadIfEventIsMissing.ReadEverything)
        );

        await Assert.ThrowsAsync<DbApiException>(async () =>
        {
            await foreach (var _ in client.ReadEventsAsync("/test", options, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(
            """{"subject":"/test","options":{"recursive":true,"order":"chronological","upperBound":{"id":"10","type":"exclusive"},"fromLatestEvent":{"subject":"/test","type":"io.eventsourcingdb.test","ifEventIsMissing":"read-everything"}}}""",
            server.Requests[1].Body
        );
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task SendsTheTraceContextOfAnEventOnlyIfItIsSet(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(HttpStatusCode.BadRequest, "refused\n"));
        var client = server.CreateClient();

        await Assert.ThrowsAsync<DbApiException>(() => client.WriteEventsAsync(
            [
                new EventCandidate("https://www.eventsourcingdb.io", "/test", "io.eventsourcingdb.test", new { value = 23 }, TraceParent: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"),
                new EventCandidate("https://www.eventsourcingdb.io", "/test", "io.eventsourcingdb.test", new { value = 42 }),
            ],
            [Precondition.IsSubjectPristinePrecondition("/test")],
            TestContext.Current.CancellationToken
        ));

        Assert.Equal(
            """{"events":[{"source":"https://www.eventsourcingdb.io","subject":"/test","type":"io.eventsourcingdb.test","data":{"value":23},"traceparent":"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"},{"source":"https://www.eventsourcingdb.io","subject":"/test","type":"io.eventsourcingdb.test","data":{"value":42}}],"preconditions":[{"type":"isSubjectPristine","payload":{"subject":"/test"}}]}""",
            server.Requests[1].Body
        );
    }
}
