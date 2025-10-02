using System.Text.Json.Serialization;

namespace EventSourcingDb.Types;

public record Subject(
    [property: JsonPropertyName("subject")] string Name
);
