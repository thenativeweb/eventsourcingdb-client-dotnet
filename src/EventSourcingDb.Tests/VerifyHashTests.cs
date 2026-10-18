using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class VerifyHashTests : EventSourcingDbTests
{
    [Fact]
    public async Task VerifyHash()
    {
        var client = Container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        var @event = writtenEvents.Single();
        @event.VerifyHash();
    }

    [Fact]
    public async Task VerifiesTheHashOfEventsWhoseDataASerializerWouldWriteDifferently()
    {
        var client = Container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new { title = "2001 – A Space Odyssey", note = "<a&b>", price = 1.50 }
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);
        writtenEvents.Single().VerifyHash();

        var readEvents = await client
            .ReadEventsAsync("/test", new ReadEventsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
        readEvents.Single().VerifyHash();
    }

    [Fact]
    public async Task ThrowsWhenHashVerificationFails()
    {
        var client = Container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

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
