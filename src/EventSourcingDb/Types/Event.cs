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

    private const string HashPrefix = "esdb:hash:";

    private readonly string _timeFromServer;
    private readonly JsonSerializerOptions _serializerOptions;

    public T? GetData<T>() => Data.Deserialize<T>(_serializerOptions);

    public object? GetData(Type type) => Data.Deserialize(type, _serializerOptions);

    public void VerifyHash()
    {
        var expectedHash = GetHashVersion() switch
        {
            null or "v1" => ComputeHashV1(),
            "v2" => ComputeHashV2(),
            var version => throw new UnsupportedHashVersionException($"Hash version '{version}' is not supported."),
        };

        if (expectedHash != Hash)
        {
            throw new HashVerificationFailedException("Hash verification failed.");
        }
    }

    // Returns null for a hash of version 1, which has no prefix, and otherwise the version that
    // the prefix names, such as v2 for esdb:hash:v2:.
    private string? GetHashVersion()
    {
        if (!Hash.StartsWith(HashPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var version = Hash[HashPrefix.Length..];
        var separatorIndex = version.IndexOf(':');

        return separatorIndex < 0 ? version : version[..separatorIndex];
    }

    private string ComputeHashV1()
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
        var metadataHashHex = ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(metadata)));
        var dataHashHex = ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(GetRawData())));

        return ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(metadataHashHex + dataHashHex)));
    }

    private string ComputeHashV2()
    {
        const string prefix = HashPrefix + "v2:";
        const string missingField = "-";

        var content = string.Join(
            "\n",
            prefix,
            SpecVersion ?? missingField,
            Id ?? missingField,
            PredecessorHash ?? missingField,
            _timeFromServer ?? missingField,
            Source ?? missingField,
            Subject ?? missingField,
            Type ?? missingField,
            DataContentType ?? missingField,
            TraceParent ?? missingField,
            TraceState ?? missingField,
            Data.ValueKind is JsonValueKind.Undefined ? missingField : GetRawData()
        );

        return prefix + ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    // The data must be hashed exactly as the server sent it. Serializing it again could escape
    // characters or format numbers differently, and the hash would no longer match.
    private string GetRawData() => Data.GetRawText();

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

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
            throw new SignatureVerificationFailedException("Signature verification failed.");
        }
    }
}
