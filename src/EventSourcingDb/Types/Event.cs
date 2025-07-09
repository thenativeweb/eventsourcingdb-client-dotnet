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
    JsonElement Data,
    string Hash,
    string PredecessorHash,
    string? TraceParent,
    string? TraceState
)
{
    private static readonly JsonSerializerOptions _defaultSerializerOptions =
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    internal Event(CloudEvent cloudEvent)
        : this(cloudEvent.SpecVersion,
            cloudEvent.Id,
            DateTimeOffset.Parse(cloudEvent.Time),
            cloudEvent.Source,
            cloudEvent.Subject,
            cloudEvent.Type,
            cloudEvent.DataContentType,
            cloudEvent.Data,
            cloudEvent.Hash,
            cloudEvent.PredecessorHash,
            cloudEvent.TraceParent,
            cloudEvent.TraceState)
    { }

    public T? GetData<T>() =>
        Data.Deserialize<T>(_defaultSerializerOptions);

    public object? GetData(Type type) =>
        Data.Deserialize(type, _defaultSerializerOptions);
}
