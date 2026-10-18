using System.Text.Json;
using System.Text.Json.Serialization;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

// The fixtures are events as EventSourcingDB returned them, byte for byte. Their data contains
// non-ASCII characters and escape sequences, which a JSON serializer would write differently.
public sealed class VerifyHashFixtureTests
{
    // Written by EventSourcingDB 1.2.0, which writes version 1 of the hash.
    private const string EventWithHashV1 = """
        {"source":"https://www.eventsourcingdb.io","subject":"/books/42","type":"io.eventsourcingdb.library.book-acquired","specversion":"1.0","id":"0","time":"2026-10-18T13:46:55.558844542Z","datacontenttype":"application/json","predecessorhash":"0000000000000000000000000000000000000000000000000000000000000000","data":{"e":"é","n":1.5,"note":"\u003ca\u0026b\u003e","title":"2001 – A Space Odyssey"},"hash":"1691e68fd4853bbe87419bffec84b1a14f135f91dbc14ab7118f79375a2d5add","signature":null}
        """;

    private const string EventWithHashV1AndTraceContext = """
        {"source":"https://www.eventsourcingdb.io","subject":"/books/42","type":"io.eventsourcingdb.library.book-borrowed","traceparent":"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01","tracestate":"rojo=00f067aa0ba902b7","specversion":"1.0","id":"1","time":"2026-10-18T13:46:55.558844542Z","datacontenttype":"application/json","predecessorhash":"1691e68fd4853bbe87419bffec84b1a14f135f91dbc14ab7118f79375a2d5add","data":{"by":"x"},"hash":"ca84ff9369689285b1c33b42aa346389f3e15abe45bee332bb1b264e4be72755","signature":null}
        """;

    private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Theory]
    [InlineData(EventWithHashV1)]
    [InlineData(EventWithHashV1AndTraceContext)]
    public void VerifiesAHashOfVersion1(string eventJson)
    {
        var @event = ParseEvent(eventJson);

        @event.VerifyHash();
    }

    // Parses the event the way the client parses a line of a stream.
    private static Event ParseEvent(string eventJson)
    {
        var line = JsonSerializer.Deserialize<Line>($$"""{"type":"event","payload":{{eventJson}}}""", _serializerOptions);
        var cloudEvent = line!.Payload.Deserialize<CloudEvent>(_serializerOptions);

        return new Event(cloudEvent!, _serializerOptions);
    }
}
