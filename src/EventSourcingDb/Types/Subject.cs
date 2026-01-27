using System.Text.Json.Serialization;

namespace EventSourcingDb.Types;

internal record Subject(
    [property: JsonPropertyName("subject")] string Name
);
