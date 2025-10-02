using System.Text.Json;

namespace EventSourcingDb.Types;

internal record Line(string Type, JsonElement Payload);
