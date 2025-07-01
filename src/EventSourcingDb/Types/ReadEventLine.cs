using System.Text.Json;

namespace EventSourcingDb.Types;

public record ReadEventLine(string Type, JsonElement Payload);
