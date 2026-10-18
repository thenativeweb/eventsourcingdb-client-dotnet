using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;

namespace EventSourcingDb.Tests;

// Runs each of the requests of the client by its name, so that a test can cover all of them.
internal static class TestRequests
{
    public const string EventJson = """{"specversion":"1.0","id":"0","time":"2026-10-03T12:00:00Z","source":"https://www.eventsourcingdb.io","subject":"/test","type":"io.eventsourcingdb.test","datacontenttype":"application/json","data":{"value":23},"hash":"","predecessorhash":""}""";

    public static readonly string[] All =
    [
        "ping",
        "verify-api-token",
        "write-events",
        "register-event-schema",
        "read-event-type",
        "read-events",
        "read-subjects",
        "read-event-types",
        "observe-events",
        "run-eventql-query",
    ];

    public static readonly string[] Streams =
    [
        "read-events",
        "read-subjects",
        "read-event-types",
        "observe-events",
        "run-eventql-query",
    ];

    public static readonly string[] Writes =
    [
        "write-events",
        "register-event-schema",
    ];

    public static async Task RunAsync(IClient client, string request, CancellationToken token)
    {
        switch (request)
        {
            case "ping":
                await client.PingAsync(token);
                break;
            case "verify-api-token":
                await client.VerifyApiTokenAsync(token);
                break;
            case "write-events":
                await client.WriteEventsAsync(
                    [new EventCandidate("https://www.eventsourcingdb.io", "/test", "io.eventsourcingdb.test", new { value = 23 })],
                    token: token
                );
                break;
            case "register-event-schema":
                await client.RegisterEventSchemaAsync("io.eventsourcingdb.test", JsonDocument.Parse("""{"type":"object"}""").RootElement, token);
                break;
            case "read-event-type":
                await client.ReadEventTypeAsync("io.eventsourcingdb.test", token);
                break;
            default:
                await foreach (var _ in GetStream(client, request, token))
                {
                }
                break;
        }
    }

    public static IAsyncEnumerable<object?> GetStream(IClient client, string request, CancellationToken token)
    {
        return request switch
        {
            "read-events" => client.ReadEventsAsync("/", new ReadEventsOptions(Recursive: true), token),
            "read-subjects" => client.ReadSubjectsAsync("/", new ReadSubjectsOptions(Recursive: true), token),
            "read-event-types" => client.ReadEventTypesAsync(token),
            "observe-events" => client.ObserveEventsAsync("/", new ObserveEventsOptions(Recursive: true), token),
            "run-eventql-query" => client.RunEventQlQueryAsync<Event>("FROM e IN events PROJECT INTO e", token),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, "Unknown stream."),
        };
    }

    // An item of a stream, as the given API sends it.
    public static string Item(string apiVersion, string type, string data)
    {
        return apiVersion switch
        {
            "v1" => $$"""{"type":"{{type}}","payload":{{data}}}""",
            "v2" => $$"""{"type":"{{type}}","data":{{data}}}""",
            _ => throw new ArgumentOutOfRangeException(nameof(apiVersion), apiVersion, "Unknown API version."),
        };
    }

    public static string Heartbeat(string apiVersion) => Item(apiVersion, "heartbeat", "{}");

    public const string End = """{"type":"end","data":{}}""";

    public static string Event(string apiVersion, string id) => Item(apiVersion, "event", EventJson.Replace("\"id\":\"0\"", $"\"id\":\"{id}\""));

    // The first item of the stream that answers the given request, as the given API sends it.
    public static string FirstItemOf(string apiVersion, string request)
    {
        return request switch
        {
            "read-events" or "observe-events" => Event(apiVersion, "0"),
            "read-subjects" => Item(apiVersion, "subject", """{"subject":"/"}"""),
            "read-event-types" => apiVersion == "v1"
                ? Item(apiVersion, "eventType", """{"eventType":"io.eventsourcingdb.test","isPhantom":false}""")
                : Item(apiVersion, "eventType", """{"type":"io.eventsourcingdb.test","hasEvents":true,"schema":null}"""),
            "run-eventql-query" => apiVersion == "v1"
                ? Item(apiVersion, "row", EventJson)
                : Item(apiVersion, "row", $$"""{"value":{{EventJson}}}"""),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, "Unknown stream."),
        };
    }
}
