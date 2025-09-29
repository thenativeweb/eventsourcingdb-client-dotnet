using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class VerifyHashTests : IAsyncLifetime
{
    private Container? _container;

    public async Task InitializeAsync()
    {
        var imageVersion = DockerfileHelper.GetImageVersionFromDockerfile();
        _container = new Container().WithImageTag(imageVersion);
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
    }

    [Fact]
    public async Task VerifyHash()
    {
        var client = _container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate]);

        var @event = writtenEvents.Single();
        @event.VerifyHash();
    }

    [Fact]
    public async Task ThrowsWhenHashVerificationFails()
    {
        var client = _container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate]);

        var @event = writtenEvents.Single();

        var invalidHash = SHA256.HashData("invalid-hash"u8.ToArray());
        var invalidHashHex = BitConverter.ToString(invalidHash).Replace("-", "").ToLowerInvariant();

        typeof(Event)
            .GetField("<Hash>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?
            .SetValue(@event, invalidHashHex);

        Assert.Throws<Exception>(() => @event.VerifyHash());
    }

    private record struct EventData(int Value);
}
