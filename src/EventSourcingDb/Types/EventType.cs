using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventSourcingDb.Types;

public record EventType(
    [property: JsonPropertyName("eventType")] string Type,
    bool IsPhantom,
    JsonElement? Schema
);
