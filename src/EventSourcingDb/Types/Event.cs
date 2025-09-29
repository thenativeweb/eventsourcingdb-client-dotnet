using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EventSourcingDb.Types;

public record Event
{
    public string SpecVersion { get; }
    public string Id { get; }
    public DateTimeOffset Time { get; }
    private readonly string _timeFromServer;
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
        _timeFromServer = cloudEvent.Time;
        Source = cloudEvent.Source;
        Subject = cloudEvent.Subject;
        Type = cloudEvent.Type;
        DataContentType = cloudEvent.DataContentType;
        Data = cloudEvent.Data;
        Hash = cloudEvent.Hash;
        PredecessorHash = cloudEvent.PredecessorHash;
        TraceParent = cloudEvent.TraceParent;
        TraceState = cloudEvent.TraceState;

        _serializerOptions = serializerOptions ?? throw new ArgumentNullException(nameof(serializerOptions));
    }

    private readonly JsonSerializerOptions _serializerOptions;

    public T? GetData<T>() => Data.Deserialize<T>(_serializerOptions);

    public object? GetData(Type type) => Data.Deserialize(type, _serializerOptions);

    public void VerifyHash()
    {
        var metadata = string.Join(
            "|",
            SpecVersion,
            Id,
            PredecessorHash,
            _timeFromServer,
            Source,
            Subject,
            Type,
            DataContentType
        );
        var metadataHash = SHA256.HashData(Encoding.UTF8.GetBytes(metadata));
        var metadataHashHex = BitConverter.ToString(metadataHash).Replace("-", "").ToLowerInvariant();

        var dataHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Data));
        var dataHashHex = BitConverter.ToString(dataHash).Replace("-", "").ToLowerInvariant();

        var finalHash = SHA256.HashData(Encoding.UTF8.GetBytes(metadataHashHex + dataHashHex));
        var finalHashHex = BitConverter.ToString(finalHash).Replace("-", "").ToLowerInvariant();

        if (finalHashHex != Hash)
        {
            throw new Exception("Hash verification failed.");
        }
    }
}
