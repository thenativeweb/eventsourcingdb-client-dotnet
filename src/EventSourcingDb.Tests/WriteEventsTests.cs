using System.Linq;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class WriteEventsTests : EventSourcingDbTests
{
    [Fact]
    public async Task WritesASingleEvent()
    {
        var client = Container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        Assert.Single(writtenEvents);
        Assert.Collection(writtenEvents, writtenEvent => Assert.Equal("0", writtenEvent.Id));
    }

    [Fact]
    public async Task WritesMultipleEvents()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        var writtenEvents = await client.WriteEventsAsync([firstEvent, secondEvent], token: TestContext.Current.CancellationToken);

        Assert.Equal(2, writtenEvents.Count);
        Assert.Collection(writtenEvents,
            writtenEvent =>
            {
                Assert.Equal("0", writtenEvent.Id);
                Assert.Equal(firstData, writtenEvent.GetData<EventData>());
            },
            writtenEvent =>
            {
                Assert.Equal("1", writtenEvent.Id);
                Assert.Equal(secondData, writtenEvent.GetData<EventData>());
            }
        );
    }

    [Fact]
    public async Task WritesTheTraceContextOfAnEvent()
    {
        var client = Container!.GetClient();

        const string traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        const string traceState = "rojo=00f067aa0ba902b7";

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42),
            TraceParent: traceParent,
            TraceState: traceState
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        Assert.Collection(writtenEvents, writtenEvent =>
        {
            Assert.Equal(traceParent, writtenEvent.TraceParent);
            Assert.Equal(traceState, writtenEvent.TraceState);
        });

        var readEvents = await client
            .ReadEventsAsync("/test", new ReadEventsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Collection(readEvents, readEvent =>
        {
            Assert.Equal(traceParent, readEvent.TraceParent);
            Assert.Equal(traceState, readEvent.TraceState);
        });
    }

    [Fact]
    public async Task SupportsTheIsSubjectPristinePrecondition()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent], token: TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<DbApiException>(async () =>
            await client.WriteEventsAsync(
                [secondEvent],
                [Precondition.IsSubjectPristinePrecondition("/test")],
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);
    }

    [Fact]
    public async Task SupportsTheIsSubjectPopulatedPrecondition()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        var error = await Assert.ThrowsAsync<DbApiException>(async () =>
            await client.WriteEventsAsync(
                [secondEvent],
                [Precondition.IsSubjectPopulatedPrecondition("/test")],
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);

        await client.WriteEventsAsync([firstEvent], token: TestContext.Current.CancellationToken);

        var writtenEvents = await client.WriteEventsAsync(
            [secondEvent],
            [Precondition.IsSubjectPopulatedPrecondition("/test")],
            TestContext.Current.CancellationToken
        );

        Assert.Single(writtenEvents);
        Assert.Equal("1", writtenEvents.Single().Id);
    }

    [Fact]
    public async Task SupportsTheIsSubjectOnEventIdPrecondition()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent], token: TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<DbApiException>(async () =>
            await client.WriteEventsAsync(
                [secondEvent],
                [Precondition.IsSubjectOnEventIdPrecondition("/test", "1")],
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);
    }

    [Fact]
    public async Task SupportsTheIsSubjectPristinePreconditionWithOptionsThatAreNotRecursive()
    {
        var client = Container!.GetClient();
        var options = new PreconditionOptions(Recursive: false);

        await client.WriteEventsAsync([CreateEvent("/test/1")], token: TestContext.Current.CancellationToken);

        // Only the subject itself counts, so an event below it does not violate the precondition.
        var writtenEvents = await client.WriteEventsAsync(
            [CreateEvent("/test")],
            [Precondition.IsSubjectPristinePrecondition("/test", options)],
            TestContext.Current.CancellationToken
        );

        Assert.Single(writtenEvents);

        var error = await Assert.ThrowsAsync<DbApiException>(() => client.WriteEventsAsync(
            [CreateEvent("/test/1")],
            [Precondition.IsSubjectPristinePrecondition("/test/1", options)],
            TestContext.Current.CancellationToken
        ));

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);
    }

    [Fact]
    public async Task SupportsTheIsSubjectPristinePreconditionRecursively()
    {
        var client = Container!.GetClient();
        var options = new PreconditionOptions(Recursive: true);

        await client.WriteEventsAsync([CreateEvent("/test/1")], token: TestContext.Current.CancellationToken);

        if (!await OffersTheV2ApiAsync())
        {
            await AssertFailsWithoutTheV2ApiAsync(client, Precondition.IsSubjectPristinePrecondition("/test", options));
            return;
        }

        var error = await Assert.ThrowsAsync<DbApiException>(() => client.WriteEventsAsync(
            [CreateEvent("/test")],
            [Precondition.IsSubjectPristinePrecondition("/test", options)],
            TestContext.Current.CancellationToken
        ));

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);

        // Only the subject and the subjects below it count, so an event of a sibling does not
        // violate the precondition.
        var writtenEvents = await client.WriteEventsAsync(
            [CreateEvent("/test/2")],
            [Precondition.IsSubjectPristinePrecondition("/test/2", options)],
            TestContext.Current.CancellationToken
        );

        Assert.Single(writtenEvents);
    }

    [Fact]
    public async Task SupportsTheIsSubjectPopulatedPreconditionWithOptionsThatAreNotRecursive()
    {
        var client = Container!.GetClient();
        var options = new PreconditionOptions(Recursive: false);

        await client.WriteEventsAsync([CreateEvent("/test/1")], token: TestContext.Current.CancellationToken);

        // Only the subject itself counts, so an event below it does not fulfill the precondition.
        var error = await Assert.ThrowsAsync<DbApiException>(() => client.WriteEventsAsync(
            [CreateEvent("/test")],
            [Precondition.IsSubjectPopulatedPrecondition("/test", options)],
            TestContext.Current.CancellationToken
        ));

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);

        var writtenEvents = await client.WriteEventsAsync(
            [CreateEvent("/test/1")],
            [Precondition.IsSubjectPopulatedPrecondition("/test/1", options)],
            TestContext.Current.CancellationToken
        );

        Assert.Single(writtenEvents);
    }

    [Fact]
    public async Task SupportsTheIsSubjectPopulatedPreconditionRecursively()
    {
        var client = Container!.GetClient();
        var options = new PreconditionOptions(Recursive: true);

        await client.WriteEventsAsync([CreateEvent("/test/1")], token: TestContext.Current.CancellationToken);

        if (!await OffersTheV2ApiAsync())
        {
            await AssertFailsWithoutTheV2ApiAsync(client, Precondition.IsSubjectPopulatedPrecondition("/test", options));
            return;
        }

        var writtenEvents = await client.WriteEventsAsync(
            [CreateEvent("/test")],
            [Precondition.IsSubjectPopulatedPrecondition("/test", options)],
            TestContext.Current.CancellationToken
        );

        Assert.Single(writtenEvents);

        // Only the subject and the subjects below it count, so an event of a sibling does not
        // fulfill the precondition.
        var error = await Assert.ThrowsAsync<DbApiException>(() => client.WriteEventsAsync(
            [CreateEvent("/test/2")],
            [Precondition.IsSubjectPopulatedPrecondition("/test/2", options)],
            TestContext.Current.CancellationToken
        ));

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);
    }

    [Fact]
    public async Task SupportsTheIsSubjectOnEventIdPreconditionWithOptionsThatAreNotRecursive()
    {
        var client = Container!.GetClient();
        var options = new PreconditionOptions(Recursive: false);

        await client.WriteEventsAsync([CreateEvent("/test"), CreateEvent("/test/1")], token: TestContext.Current.CancellationToken);

        // Only the subject itself counts, so its latest event is the one with the ID 0.
        var error = await Assert.ThrowsAsync<DbApiException>(() => client.WriteEventsAsync(
            [CreateEvent("/test")],
            [Precondition.IsSubjectOnEventIdPrecondition("/test", "1", options)],
            TestContext.Current.CancellationToken
        ));

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);

        var writtenEvents = await client.WriteEventsAsync(
            [CreateEvent("/test")],
            [Precondition.IsSubjectOnEventIdPrecondition("/test", "0", options)],
            TestContext.Current.CancellationToken
        );

        Assert.Single(writtenEvents);
    }

    [Fact]
    public async Task SupportsTheIsSubjectOnEventIdPreconditionRecursively()
    {
        var client = Container!.GetClient();
        var options = new PreconditionOptions(Recursive: true);

        await client.WriteEventsAsync([CreateEvent("/test"), CreateEvent("/test/1")], token: TestContext.Current.CancellationToken);

        if (!await OffersTheV2ApiAsync())
        {
            await AssertFailsWithoutTheV2ApiAsync(client, Precondition.IsSubjectOnEventIdPrecondition("/test", "1", options));
            return;
        }

        // The subjects below the subject count as well, so the latest event is the one with the ID
        // 1, which belongs to a subject below it.
        var error = await Assert.ThrowsAsync<DbApiException>(() => client.WriteEventsAsync(
            [CreateEvent("/test")],
            [Precondition.IsSubjectOnEventIdPrecondition("/test", "0", options)],
            TestContext.Current.CancellationToken
        ));

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);

        var writtenEvents = await client.WriteEventsAsync(
            [CreateEvent("/test")],
            [Precondition.IsSubjectOnEventIdPrecondition("/test", "1", options)],
            TestContext.Current.CancellationToken
        );

        Assert.Single(writtenEvents);
    }

    [Fact]
    public async Task SupportsTheIsEventQlQueryTruePrecondition()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent], token: TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<DbApiException>(async () =>
            await client.WriteEventsAsync(
                [secondEvent],
                [Precondition.IsEventQlQueryTruePrecondition("FROM e IN events PROJECT INTO COUNT() == 0")],
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(ErrorCodes.PreconditionViolated, error.Code);
    }

    [Fact]
    public async Task DeserializesEventDataCorrectly()
    {
        var client = Container!.GetClient();

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        var data = writtenEvents[0].GetData(typeof(EventData));

        Assert.IsType<EventData>(data);
        Assert.Equal(eventCandidate.Data, data);
    }

    [Fact]
    public async Task PassesErrorMessageInException()
    {
        var client = Container!.GetClient();

        const string invalidSubject = "test"; // Subjects must start with a slash

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: invalidSubject,
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );

        var writeEvents = () => client.WriteEventsAsync([eventCandidate], token: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<DbApiException>(writeEvents);

        Assert.Contains("subject", ex.Message);
    }

    // A server without the v2 API cannot check a recursive precondition, so a write with one fails
    // before anything of it is sent, and stores nothing.
    private static async Task AssertFailsWithoutTheV2ApiAsync(IClient client, Precondition recursivePrecondition)
    {
        var eventsBefore = await ReadAllEventsAsync(client);

        var error = await Assert.ThrowsAsync<IncompatibleServerException>(() => client.WriteEventsAsync(
            [CreateEvent("/test")],
            [recursivePrecondition],
            TestContext.Current.CancellationToken
        ));

        Assert.Equal("The server does not offer the v2 API, which a recursive precondition needs.", error.Message);
        Assert.Equal(eventsBefore, await ReadAllEventsAsync(client));
    }

    private static async Task<string[]> ReadAllEventsAsync(IClient client)
    {
        var events = await client
            .ReadEventsAsync("/", new ReadEventsOptions(Recursive: true), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        return events.Select(@event => $"{@event.Id} {@event.Subject}").ToArray();
    }

    private static EventCandidate CreateEvent(string subject)
    {
        return new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: subject,
            Type: "io.eventsourcingdb.test",
            Data: new EventData(42)
        );
    }

    private record struct EventData(int Value);
}
