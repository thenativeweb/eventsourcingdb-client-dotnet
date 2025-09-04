using System;
using System.Text.Json;

namespace EventSourcingDb.Types;

public record Event
{
    public string SpecVersion { get; }
    public string Id { get; }
    public DateTimeOffset Time { get; }
    public string Source { get; }
    public string Subject { get; }
    public string Type { get; }
    public string DataContentType { get; }
    public JsonElement Data { get; }
    public string Hash { get; }
    public string PredecessorHash { get; }
    public string? TraceParent { get; }
    public string? TraceState { get; }

    internal Event(CloudEvent cloudEvent, JsonSerializerOptions serializerOptions)
    {
        SpecVersion = cloudEvent.SpecVersion;
        Id = cloudEvent.Id;
        Time = DateTimeOffset.Parse(cloudEvent.Time);
        Source = cloudEvent.Source;
        Subject = cloudEvent.Subject;
        Type = cloudEvent.Type;
        DataContentType = cloudEvent.DataContentType;
        Data = cloudEvent.Data;
        Hash = cloudEvent.Hash;
        PredecessorHash = cloudEvent.PredecessorHash;
        TraceParent = cloudEvent.TraceParent;
        TraceState = cloudEvent.TraceState;

        _serializerOptions = serializerOptions;
    }

    private readonly JsonSerializerOptions _serializerOptions;

    public T? GetData<T>() => Data.Deserialize<T>(_serializerOptions);

    public object? GetData(Type type) => Data.Deserialize(type, _serializerOptions);
}
