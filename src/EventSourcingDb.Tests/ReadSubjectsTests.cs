using System;
using System.Linq;
using System.Net;
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
            .ReadSubjectsAsync("/", new ReadSubjectsOptions(Recursive: true), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(subjectsRead);
    }

    [Fact]
    public async Task ReadsAllSubjectsRecursively()
    {
        var client = Container!.GetClient();
        await WriteEventsAsync(client);

        var subjectsRead = await client
            .ReadSubjectsAsync("/", new ReadSubjectsOptions(Recursive: true), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Collection(subjectsRead,
            subject => Assert.Equal("/", subject),
            subject => Assert.Equal("/test", subject),
            subject => Assert.Equal("/test/1", subject),
            subject => Assert.Equal("/test/2", subject)
        );
    }

    [Fact]
    public async Task ReadsTheSubjectAndAllSubjectsBelowItRecursively()
    {
        var client = Container!.GetClient();
        await WriteEventsAsync(client);

        var subjectsRead = await client
            .ReadSubjectsAsync("/test", new ReadSubjectsOptions(Recursive: true), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Collection(subjectsRead,
            subject => Assert.Equal("/test", subject),
            subject => Assert.Equal("/test/1", subject),
            subject => Assert.Equal("/test/2", subject)
        );
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/test")]
    [InlineData("/test/1")]
    public async Task ReadsOnlyTheSubjectItselfIfNotRecursive(string subject)
    {
        var client = Container!.GetClient();
        await WriteEventsAsync(client);

        var subjectsRead = await client
            .ReadSubjectsAsync(subject, new ReadSubjectsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([subject], subjectsRead);
    }

    [Theory]
    [InlineData("/test/1/", "/test/1")]
    [InlineData("/test/", "/test")]
    public async Task ReadsTheSubjectItselfAsTheServerNormalizesItIfNotRecursive(string subject, string expectedSubject)
    {
        var client = Container!.GetClient();
        await WriteEventsAsync(client);

        var subjectsRead = await client
            .ReadSubjectsAsync(subject, new ReadSubjectsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([expectedSubject], subjectsRead);
    }

    [Fact]
    public async Task ReadsNoSubjectIfNotRecursiveAndTheSubjectDoesNotExist()
    {
        var client = Container!.GetClient();
        await WriteEventsAsync(client);

        var subjectsRead = await client
            .ReadSubjectsAsync("/other", new ReadSubjectsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(subjectsRead);
    }

    private static async Task WriteEventsAsync(IClient client)
    {
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
    }

    private record struct EventData(int Value);
}

public sealed class ReadSubjectsRequestTests
{
    private static readonly string[] _v1Subjects = ["/test", "/test/1", "/test/1/a", "/test/2"];

    [Theory]
    [InlineData(true, """{"subject":"/test","options":{"recursive":true}}""")]
    [InlineData(false, """{"subject":"/test","options":{"recursive":false}}""")]
    public async Task SendsTheSubjectAndTheRecursiveOptionToTheV2Api(bool recursive, string expectedBody)
    {
        var server = FakeServer.ForApi("v2", _ => FakeServer.Ndjson(
            TestRequests.Item("v2", "subject", """{"subject":"/test"}"""),
            TestRequests.End
        ));
        var client = server.CreateClient();

        var subjectsRead = await client
            .ReadSubjectsAsync("/test", new ReadSubjectsOptions(recursive), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["/test"], subjectsRead);
        Assert.Equal(new FakeRequest("QUERY", "/api/v2/data/read-subjects", expectedBody), server.Requests[^1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadsTheSubjectsRecursivelyFromTheV1Api(bool recursive)
    {
        var server = FakeServer.ForApi("v1", _ => FakeServer.Ndjson(
            _v1Subjects.Select(subject => TestRequests.Item("v1", "subject", $$"""{"subject":"{{subject}}"}""")).ToArray()
        ));
        var client = server.CreateClient();

        await client
            .ReadSubjectsAsync("/test", new ReadSubjectsOptions(recursive), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new FakeRequest("POST", "/api/v1/read-subjects", """{"baseSubject":"/test"}"""), server.Requests[^1]);
    }

    [Fact]
    public async Task KeepsEverySubjectThatTheV1ApiReadsIfRecursive()
    {
        var server = FakeServer.ForApi("v1", _ => FakeServer.Ndjson(
            _v1Subjects.Select(subject => TestRequests.Item("v1", "subject", $$"""{"subject":"{{subject}}"}""")).ToArray()
        ));
        var client = server.CreateClient();

        var subjectsRead = await client
            .ReadSubjectsAsync("/test", new ReadSubjectsOptions(Recursive: true), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_v1Subjects, subjectsRead);
    }

    [Theory]
    [InlineData("/test")]
    [InlineData("/test/1")]
    [InlineData("/test/2")]
    public async Task KeepsOnlyTheSubjectItselfOfThoseThatTheV1ApiReadsIfNotRecursive(string subject)
    {
        // The v1 API reads the subjects below the base subject as well, in whatever order.
        var server = FakeServer.ForApi("v1", _ => FakeServer.Ndjson(
            Enumerable.Reverse(_v1Subjects).Select(readSubject => TestRequests.Item("v1", "subject", $$"""{"subject":"{{readSubject}}"}""")).ToArray()
        ));
        var client = server.CreateClient();

        var subjectsRead = await client
            .ReadSubjectsAsync(subject, new ReadSubjectsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([subject], subjectsRead);
    }

    [Theory]
    [InlineData("/test/1/", "/test/1")]
    [InlineData("/test/", "/test")]
    [InlineData("/", "/")]
    public async Task KeepsTheSubjectAsTheServerNormalizesItOfThoseThatTheV1ApiReadsIfNotRecursive(string subject, string expectedSubject)
    {
        // The server reads a subject as a slash followed by the subject without its leading and
        // trailing slashes, and lists it that way.
        var server = FakeServer.ForApi("v1", _ => FakeServer.Ndjson(
            new[] { "/", "/test", "/test/1", "/test/1/a" }.Select(readSubject => TestRequests.Item("v1", "subject", $$"""{"subject":"{{readSubject}}"}""")).ToArray()
        ));
        var client = server.CreateClient();

        var subjectsRead = await client
            .ReadSubjectsAsync(subject, new ReadSubjectsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([expectedSubject], subjectsRead);
    }

    [Fact]
    public async Task KeepsNoSubjectOfThoseThatTheV1ApiReadsIfNotRecursiveAndTheSubjectIsNotAmongThem()
    {
        var server = FakeServer.ForApi("v1", _ => FakeServer.Ndjson(
            TestRequests.Item("v1", "subject", """{"subject":"/test/1"}""")
        ));
        var client = server.CreateClient();

        var subjectsRead = await client
            .ReadSubjectsAsync("/test", new ReadSubjectsOptions(Recursive: false), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(subjectsRead);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task FailsLoudlyWithoutOptions(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ => throw new InvalidOperationException("No request expected."));
        var client = server.CreateClient();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in client.ReadSubjectsAsync("/", null!, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task FailsLikeAnyOtherRequestIfTheV1ApiRefusesIt()
    {
        var server = FakeServer.ForApi("v1", _ => FakeServer.Text(HttpStatusCode.BadRequest, "malformed subject\n"));
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(async () =>
        {
            await foreach (var _ in client.ReadSubjectsAsync("test", new ReadSubjectsOptions(Recursive: false), TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, exception.Status);
    }
}
