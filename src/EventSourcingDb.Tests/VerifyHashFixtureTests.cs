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

    // Written by a preview of EventSourcingDB 1.3, which writes version 2 of the hash.
    private const string EventWithHashV2 = """
        {"source":"https://www.eventsourcingdb.io","subject":"/books/42","type":"io.eventsourcingdb.library.book-acquired","specversion":"1.0","id":"0","time":"2026-10-18T13:46:55.741383917Z","datacontenttype":"application/json","predecessorhash":"0000000000000000000000000000000000000000000000000000000000000000","data":{"e":"é","n":1.5,"note":"\u003ca\u0026b\u003e","title":"2001 – A Space Odyssey"},"hash":"esdb:hash:v2:b15b11df4122d623d287942430b7492185a055a17085fbe8d153024caf62c483","signature":null}
        """;

    private const string EventWithHashV2AndTraceContext = """
        {"source":"https://www.eventsourcingdb.io","subject":"/books/42","type":"io.eventsourcingdb.library.book-borrowed","traceparent":"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01","tracestate":"rojo=00f067aa0ba902b7","specversion":"1.0","id":"1","time":"2026-10-18T13:46:55.741383917Z","datacontenttype":"application/json","predecessorhash":"esdb:hash:v2:b15b11df4122d623d287942430b7492185a055a17085fbe8d153024caf62c483","data":{"by":"x"},"hash":"esdb:hash:v2:d9e79e1a2a4e84d7d810ca0541dde181eebb8689af6580df68457c0a699b2821","signature":null}
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

    [Theory]
    [InlineData("Odyssey", "Odyssee")]
    [InlineData("/books/42", "/books/43")]
    [InlineData("2026-10-18T13:46:55.558844542Z", "2026-10-18T13:46:55.558844543Z")]
    public void ThrowsWhenAHashOfVersion1DoesNotMatch(string original, string tampered)
    {
        Assert.Contains(original, EventWithHashV1);
        var @event = ParseEvent(EventWithHashV1.Replace(original, tampered));

        var exception = Assert.Throws<HashVerificationFailedException>(() => @event.VerifyHash());
        Assert.Equal("Hash verification failed.", exception.Message);
    }

    [Fact]
    public void IgnoresTheTraceContextInAHashOfVersion1()
    {
        // Version 1 of the hash does not cover traceparent and tracestate.
        var @event = ParseEvent(EventWithHashV1AndTraceContext.Replace("b7ad6b7169203331", "c7ad6b7169203331"));

        @event.VerifyHash();
    }

    [Theory]
    [InlineData(EventWithHashV2)]
    [InlineData(EventWithHashV2AndTraceContext)]
    public void VerifiesAHashOfVersion2(string eventJson)
    {
        var @event = ParseEvent(eventJson);

        @event.VerifyHash();
    }

    [Theory]
    [InlineData(EventWithHashV2, "Odyssey", "Odyssee")]
    [InlineData(EventWithHashV2, "/books/42", "/books/43")]
    [InlineData(EventWithHashV2, "2026-10-18T13:46:55.741383917Z", "2026-10-18T13:46:55.741383918Z")]
    [InlineData(EventWithHashV2AndTraceContext, "b7ad6b7169203331", "c7ad6b7169203331")]
    [InlineData(EventWithHashV2AndTraceContext, "rojo=00f067aa0ba902b7", "rojo=10f067aa0ba902b7")]
    [InlineData(EventWithHashV2AndTraceContext, "\"tracestate\":\"rojo=00f067aa0ba902b7\",", "")]
    public void ThrowsWhenAHashOfVersion2DoesNotMatch(string eventJson, string original, string tampered)
    {
        Assert.Contains(original, eventJson);
        var @event = ParseEvent(eventJson.Replace(original, tampered));

        var exception = Assert.Throws<HashVerificationFailedException>(() => @event.VerifyHash());
        Assert.Equal("Hash verification failed.", exception.Message);
    }

    [Fact]
    public void ThrowsWhenTheVersionOfTheHashIsUnknown()
    {
        var @event = ParseEvent(EventWithHashV2.Replace("esdb:hash:v2:", "esdb:hash:v3:"));

        var exception = Assert.Throws<UnsupportedHashVersionException>(() => @event.VerifyHash());
        Assert.Equal("Hash version 'v3' is not supported.", exception.Message);
    }

    // Parses the event the way the client parses a line of a stream.
    private static Event ParseEvent(string eventJson)
    {
        var line = JsonSerializer.Deserialize<Line>($$"""{"type":"event","payload":{{eventJson}}}""", _serializerOptions);
        var cloudEvent = line!.Payload.Deserialize<CloudEvent>(_serializerOptions);

        return new Event(cloudEvent!, _serializerOptions);
    }
}
