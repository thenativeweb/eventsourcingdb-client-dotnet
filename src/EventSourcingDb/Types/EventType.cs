using System.Text.Json;

namespace EventSourcingDb.Types;

/// <summary>
/// An event type, whether it has events, and the schema registered for it, if any.
/// </summary>
public record EventType(
    string Type,
    bool HasEvents,
    JsonElement? Schema
);
