using System.Text.Json;

namespace EventSourcingDb.Types;

internal record ReadEventLine(string Type, JsonElement Payload);
