using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;

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
    public string? Signature { get; }

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
        Signature = cloudEvent.Signature;

        _timeFromServer = cloudEvent.Time;
        _serializerOptions = serializerOptions ?? throw new ArgumentNullException(nameof(serializerOptions));
    }

    private readonly string _timeFromServer;
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

    public void VerifySignature(byte[] verificationKey)
    {
        if (Signature is null)
        {
            throw new Exception("Signature must not be null.");
        }

        VerifyHash();

        const string signaturePrefix = "esdb:signature:v1:";

        if (!Signature.StartsWith(signaturePrefix))
        {
            throw new Exception($"Signature must start with '{signaturePrefix}'.");
        }

        var signatureHex = Signature[signaturePrefix.Length..];
        var signatureBytes = Convert.FromHexString(signatureHex);

        var hashBytes = Encoding.UTF8.GetBytes(Hash);

        var algorithm = SignatureAlgorithm.Ed25519;

        var publicKey = PublicKey.Import(algorithm, verificationKey, KeyBlobFormat.PkixPublicKey);

        bool isSignatureValid = algorithm.Verify(publicKey, hashBytes, signatureBytes);
        if (!isSignatureValid)
        {
            throw new Exception("Signature verification failed.");
        }
    }
}
