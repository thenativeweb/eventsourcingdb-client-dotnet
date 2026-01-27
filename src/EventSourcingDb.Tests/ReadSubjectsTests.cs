using System.Linq;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class ReadSubjectsTests : EventSourcingDbTests
{
    [Fact]
    public async Task ReadsNoSubjectsIfTheDatabaseIsEmpty()
    {
        var client = Container!.GetClient();
        var subjectsRead = await client
            .ReadSubjectsAsync("/", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(subjectsRead);
    }

    [Fact]
    public async Task ReadsAllEventsFromTheGivenSubject()
    {
        var client = Container!.GetClient();

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test/1",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(23)
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test/2",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        await client.WriteEventsAsync([firstEvent, secondEvent], token: TestContext.Current.CancellationToken);

        var subjectsRead = await client
            .ReadSubjectsAsync("/", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Collection(subjectsRead,
            subject => Assert.Equal("/", subject),
            subject => Assert.Equal("/test", subject),
            subject => Assert.Equal("/test/1", subject),
            subject => Assert.Equal("/test/2", subject)
        );
    }

    [Fact]
    public async Task ReadsAllSubjectsFromTheBaseSubject()
    {
        var client = Container!.GetClient();

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test/1",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(23)
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test/2",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        await client.WriteEventsAsync([firstEvent, secondEvent], token: TestContext.Current.CancellationToken);

        var subjectsRead = await client
            .ReadSubjectsAsync("/test", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Collection(subjectsRead,
            subject => Assert.Equal("/test", subject),
            subject => Assert.Equal("/test/1", subject),
            subject => Assert.Equal("/test/2", subject)
        );
    }

    private record struct EventData(int Value);
}
