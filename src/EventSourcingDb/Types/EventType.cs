using System.Text.Json;

namespace EventSourcingDb.Types;

public record EventType(
    string Eventtype,
    bool IsPhantom,
    JsonElement? Schema
);
