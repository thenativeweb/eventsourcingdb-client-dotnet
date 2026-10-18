using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using NSec.Cryptography;
using Xunit;

namespace EventSourcingDb.Tests;

public class VerifySignatureTests : IAsyncDisposable
{
    private Container? _container;

    [Fact]
    public async Task ThrowsWhenEventIsNotSigned()
    {
        var imageTag = DockerfileHelper.GetImageTag();

        _container = new Container()
            .WithImageTag(imageTag);

        await _container.StartAsync(TestContext.Current.CancellationToken);

        var client = _container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        var @event = writtenEvents.Single();

        Assert.Null(@event.Signature);

        var key = Key.Create(
            SignatureAlgorithm.Ed25519,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport }
        );
        var signingKey = key.Export(KeyBlobFormat.PkixPublicKey);

        Assert.Throws<Exception>(() => @event.VerifySignature(signingKey));
    }

    [Fact]
    public async Task ThrowsWhenSignatureValidationFails()
    {
        var imageTag = DockerfileHelper.GetImageTag();

        _container = new Container()
            .WithImageTag(imageTag)
            .WithSigningKey();

        await _container.StartAsync(TestContext.Current.CancellationToken);

        var client = _container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        var @event = writtenEvents.Single();

        var tamperedSignature = @event.Signature + "0123456789abcdef";

        typeof(Event)
            .GetField("<Signature>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?
            .SetValue(@event, tamperedSignature);

        Assert.Throws<SignatureVerificationFailedException>(() => @event.VerifySignature(_container!.GetVerificationKey()));
    }

    [Fact]
    public async Task VerifySignature()
    {
        var imageTag = DockerfileHelper.GetImageTag();

        _container = new Container()
            .WithImageTag(imageTag)
            .WithSigningKey();

        await _container.StartAsync(TestContext.Current.CancellationToken);

        var client = _container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        var @event = writtenEvents.Single();
        @event.VerifySignature(_container!.GetVerificationKey());
    }

    [Fact]
    public async Task VerifiesTheSignatureOfEventsWithTraceContextAndDataASerializerWouldWriteDifferently()
    {
        var imageTag = DockerfileHelper.GetImageTag();

        _container = new Container()
            .WithImageTag(imageTag)
            .WithSigningKey();

        await _container.StartAsync(TestContext.Current.CancellationToken);

        var client = _container!.GetClient();
        var verificationKey = _container.GetVerificationKey();

        EventCandidate[] eventCandidates =
        [
            new EventCandidate(
                Source: "https://www.eventsourcingdb.io",
                Subject: "/test",
                Type: "io.eventsourcingdb.test",
                Data: new { title = "2001 – A Space Odyssey", note = "<a&b>" }
            ),
            new EventCandidate(
                Source: "https://www.eventsourcingdb.io",
                Subject: "/test",
                Type: "io.eventsourcingdb.test",
                Data: new EventData(42),
                TraceParent: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                TraceState: "rojo=00f067aa0ba902b7"
            )
        ];

        var writtenEvents = await client.WriteEventsAsync(eventCandidates, token: TestContext.Current.CancellationToken);
        var readEvents = await client
            .ReadEventsAsync("/test", new ReadEventsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, writtenEvents.Count);
        Assert.Equal(2, readEvents.Count);

        foreach (var @event in writtenEvents.Concat(readEvents))
        {
            @event.VerifySignature(verificationKey);
        }
    }

    [Fact]
    public async Task GetSigningKeyReturnsTheKeyTheVerificationKeyBelongsTo()
    {
        var imageTag = DockerfileHelper.GetImageTag();

        _container = new Container()
            .WithImageTag(imageTag)
            .WithSigningKey();

        await _container.StartAsync(TestContext.Current.CancellationToken);

        using var signingKey = Key.Import(SignatureAlgorithm.Ed25519, _container.GetSigningKey(), KeyBlobFormat.PkixPrivateKey);

        Assert.Equal(_container.GetVerificationKey(), signingKey.PublicKey.Export(KeyBlobFormat.PkixPublicKey));
    }

    [Fact]
    public async Task GetSigningKeyThrowsWithoutASigningKey()
    {
        var imageTag = DockerfileHelper.GetImageTag();

        _container = new Container()
            .WithImageTag(imageTag);

        await _container.StartAsync(TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() => _container.GetSigningKey());
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
    }

    private record struct EventData(int Value);
}
