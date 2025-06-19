using System;
using System.Text.Json;

namespace EventSourcingDb.Types;

public record Event(
    string SpecVersion,
    string Id,
    DateTimeOffset Time,
    string Source,
    string Subject,
    string Type,
    string DataContentType,
    object Data,
    string Hash,
    string PredecessorHash,
    string? TraceParent,
    string? TraceState
)
{
    private static readonly JsonSerializerOptions _defaultSerializerOptions =
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    public T? GetData<T>() =>
        Data switch
        {
            JsonElement element => element.Deserialize<T>(_defaultSerializerOptions),
            string text => JsonSerializer.Deserialize<T>(text, _defaultSerializerOptions),
            _ => default
        };
}
