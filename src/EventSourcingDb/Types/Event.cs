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

    public T? GetData<T>(JsonSerializerOptions? serializationOptions = null) =>
        Data.Deserialize<T>(serializationOptions);

    public object? GetData(Type type, JsonSerializerOptions? serializationOptions = null) =>
        Data.Deserialize(type, serializationOptions);
}
