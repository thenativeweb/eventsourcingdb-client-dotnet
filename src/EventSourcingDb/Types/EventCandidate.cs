using System.Text.Json.Serialization;

namespace EventSourcingDb.Types;

public record EventCandidate(
    string Source,
    string Subject,
    string Type,
    object Data,
    [property: JsonPropertyName("traceparent")] string? TraceParent = null,
    [property: JsonPropertyName("tracestate")] string? TraceState = null
);
