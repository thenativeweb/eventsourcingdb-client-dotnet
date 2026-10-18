using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class DbApiExceptionTests
{
    private const string DomainError = """{"type":"error","data":{"code":"https://docs.eventsourcingdb.io/errors/event-type-unknown","description":"event type is unknown: it has neither events nor a registered schema","details":{"value":"io.eventsourcingdb.test"}}}""";

    [Fact]
    public async Task ReportsAnErrorInTheAnswerOfTheV2ApiWithItsCodeDescriptionAndDetails()
    {
        var server = FakeServer.ForApi("v2", _ => FakeServer.Json(HttpStatusCode.OK, DomainError));
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(() => client.ReadEventTypeAsync("io.eventsourcingdb.test", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, exception.Status);
        Assert.Equal(ErrorCodes.EventTypeUnknown, exception.Code);
        Assert.Equal("event type is unknown: it has neither events nor a registered schema", exception.Description);
        Assert.Equal("event type is unknown: it has neither events nor a registered schema", exception.Message);
        Assert.NotNull(exception.Details);
        Assert.Equal("io.eventsourcingdb.test", exception.Details.Value.GetProperty("value").GetString());
        Assert.Null(exception.RetryAfter);
    }

    [Theory]
    [InlineData("read-events")]
    [InlineData("read-subjects")]
    [InlineData("read-event-types")]
    [InlineData("observe-events")]
    [InlineData("run-eventql-query")]
    public async Task ReportsAnErrorWithinAStreamOfTheV2ApiWithItsCodeDescriptionAndDetails(string request)
    {
        var server = FakeServer.ForApi("v2", _ => FakeServer.Ndjson(
            TestRequests.Heartbeat("v2"),
            TestRequests.FirstItemOf("v2", request),
            """{"type":"error","data":{"code":"https://docs.eventsourcingdb.io/errors/store-read-only","description":"the store is in read-only mode","details":{"operation":"read"}}}""",
            TestRequests.End
        ));
        var client = server.CreateClient();
        var itemCount = 0;

        var exception = await Assert.ThrowsAsync<DbApiException>(async () =>
        {
            await foreach (var _ in TestRequests.GetStream(client, request, TestContext.Current.CancellationToken))
            {
                itemCount++;
            }
        });

        Assert.Equal(1, itemCount);
        Assert.Equal(HttpStatusCode.OK, exception.Status);
        Assert.Equal(ErrorCodes.StoreReadOnly, exception.Code);
        Assert.Equal("the store is in read-only mode", exception.Description);
        Assert.Equal("read", exception.Details!.Value.GetProperty("operation").GetString());
    }

    [Theory]
    [InlineData("v1", "ping")]
    [InlineData("v1", "verify-api-token")]
    [InlineData("v1", "read-event-type")]
    [InlineData("v1", "read-events")]
    [InlineData("v1", "observe-events")]
    [InlineData("v2", "ping")]
    [InlineData("v2", "verify-api-token")]
    [InlineData("v2", "read-event-type")]
    [InlineData("v2", "read-events")]
    [InlineData("v2", "observe-events")]
    public async Task ReportsAStatusOtherThan200WithItsTextAsTheDescriptionAndWithoutACode(string apiVersion, string request)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(HttpStatusCode.Unauthorized, "unauthorized\n"));
        var client = server.CreateClient();

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        var dbApiException = Assert.IsType<DbApiException>(exception);
        Assert.Equal(HttpStatusCode.Unauthorized, dbApiException.Status);
        Assert.Equal("unauthorized", dbApiException.Description);
        Assert.Null(dbApiException.Code);
        Assert.Null(dbApiException.Details);
        Assert.Null(dbApiException.RetryAfter);
    }

    [Fact]
    public async Task ReportsAnErrorWithinAStreamOfTheV1ApiWithItsDescription()
    {
        var server = FakeServer.ForApi("v1", _ => FakeServer.Ndjson(
            TestRequests.Event("v1", "0"),
            """{"type":"error","payload":{"error":"the current operation mode conflicts with the given request"}}"""
        ));
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(async () =>
        {
            await foreach (var _ in TestRequests.GetStream(client, "read-events", TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(HttpStatusCode.OK, exception.Status);
        Assert.Equal("the current operation mode conflicts with the given request", exception.Description);
        Assert.Null(exception.Code);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task ReportsHowLongToWaitAfterAStatusOf429(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ =>
        {
            var response = FakeServer.Text(HttpStatusCode.TooManyRequests, "too many requests");
            response.Headers.TryAddWithoutValidation("Retry-After", "3");
            return response;
        });
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(() => client.PingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.Status);
        Assert.Equal(TimeSpan.FromSeconds(3), exception.RetryAfter);
        Assert.Equal("too many requests", exception.Description);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task ReportsNoTimeToWaitAfterAStatusOf429WithoutRetryAfter(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Text(HttpStatusCode.TooManyRequests, "too many requests"));
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(() => client.PingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.Status);
        Assert.Null(exception.RetryAfter);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task ReportsNoTimeToWaitForAStatusOtherThan429(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ =>
        {
            var response = FakeServer.Text(HttpStatusCode.ServiceUnavailable, "the server is shutting down");
            response.Headers.TryAddWithoutValidation("Retry-After", "3");
            return response;
        });
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(() => client.PingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.Status);
        Assert.Null(exception.RetryAfter);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task NeverRetriesAStatusOf429(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ =>
        {
            var response = FakeServer.Text(HttpStatusCode.TooManyRequests, "too many requests");
            response.Headers.TryAddWithoutValidation("Retry-After", "1");
            return response;
        });
        var client = server.CreateClient();

        await Assert.ThrowsAsync<DbApiException>(() => TestRequests.RunAsync(client, "write-events", TestContext.Current.CancellationToken));

        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task DescribesAnErrorWithoutTextByItsStatus()
    {
        var server = FakeServer.ForApi("v1", _ => FakeServer.Text(HttpStatusCode.InternalServerError, "\n"));
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<DbApiException>(() => client.PingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(string.Empty, exception.Description);
        Assert.Equal("EventSourcingDB answered with the status 500.", exception.Message);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task FailsIfAnAnswerIsNotFromEventSourcingDb(string apiVersion)
    {
        var server = FakeServer.ForApi(apiVersion, _ => FakeServer.Respond(HttpStatusCode.BadGateway, "bad gateway", "text/plain", isFromEventSourcingDb: false));
        var client = server.CreateClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadEventTypeAsync("io.eventsourcingdb.test", TestContext.Current.CancellationToken));

        Assert.Equal("Server must be EventSourcingDB.", exception.Message);
    }
}
