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
        var imageVersion = DockerfileHelper.GetImageVersionFromDockerfile();

        _container = new Container()
            .WithImageTag(imageVersion);

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
        var imageVersion = DockerfileHelper.GetImageVersionFromDockerfile();

        _container = new Container()
            .WithImageTag(imageVersion)
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

        Assert.Throws<Exception>(() => @event.VerifySignature(_container!.GetVerificationKey()));
    }

    [Fact]
    public async Task VerifySignature()
    {
        var imageVersion = DockerfileHelper.GetImageVersionFromDockerfile();

        _container = new Container()
            .WithImageTag(imageVersion)
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

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
    }

    private record struct EventData(int Value);
}
