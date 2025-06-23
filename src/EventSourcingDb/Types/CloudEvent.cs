using System.Text.Json;

namespace EventSourcingDb.Types;

internal record CloudEvent(
    string SpecVersion,
    string Id,
    string Time,
    string Source,
    string Subject,
    string Type,
    string DataContentType,
    JsonElement Data,
    string Hash,
    string PredecessorHash,
    string? TraceParent = null,
    string? TraceState = null
);
