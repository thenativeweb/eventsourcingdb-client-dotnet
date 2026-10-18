using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

// The v1 API reports errors without a code. Those that clearly match an error of the v2 API get
// its code, judged by the texts that EventSourcingDB 1.2.0 sends.
public sealed class V1ErrorMappingTests
{
    // The nine rows, each with a text exactly as EventSourcingDB 1.2.0 sends it. The texts come
    // from old/storage/errors.go, write_events.go, register_event_schema.go, and read_events.go at
    // tag v1.2.0, and are matched from their start.
    public static TheoryData<string, HttpStatusCode, string, string> MappedErrors => new()
    {
        { "write-events", HttpStatusCode.Conflict, "state conflict: precondition failed\n", ErrorCodes.PreconditionViolated },
        { "write-events", HttpStatusCode.Conflict, "state conflict: exceeded max store capacity\n", ErrorCodes.CapacityExceeded },
        { "write-events", HttpStatusCode.Conflict, "schema conflict: event candidate does not match schema: expected string, but got number\n", ErrorCodes.EventSchemaViolated },
        { "register-event-schema", HttpStatusCode.Conflict, "schema conflict: schema already exists\n", ErrorCodes.EventSchemaAlreadyRegistered },
        { "register-event-schema", HttpStatusCode.Conflict, "schema conflict: event with ID 0: expected string, but got number\n", ErrorCodes.EventSchemaViolated },
        { "read-events", HttpStatusCode.Conflict, "state conflict: fromLatestEvent results in an event ID greater than upperBound ID\n", ErrorCodes.RangeUnsatisfiable },
        { "read-event-type", HttpStatusCode.NotFound, "event type 'io.eventsourcingdb.test' not found\n", ErrorCodes.EventTypeUnknown },
        { "write-events", HttpStatusCode.InsufficientStorage, "insufficient storage\n", ErrorCodes.LicenseInvalid },
        { "ping", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: writing is not permitted\n", ErrorCodes.StoreReadOnly },
        { "verify-api-token", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: writing is not permitted\n", ErrorCodes.StoreReadOnly },
        { "write-events", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: writing is not permitted\n", ErrorCodes.StoreReadOnly },
        { "register-event-schema", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: writing is not permitted\n", ErrorCodes.StoreReadOnly },
        { "read-event-type", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: reading is not permitted\n", ErrorCodes.StoreReadOnly },
        { "read-events", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: reading is not permitted\n", ErrorCodes.StoreReadOnly },
        { "read-subjects", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: reading is not permitted\n", ErrorCodes.StoreReadOnly },
        { "read-event-types", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: reading is not permitted\n", ErrorCodes.StoreReadOnly },
        { "observe-events", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: reading is not permitted\n", ErrorCodes.StoreReadOnly },
        { "run-eventql-query", HttpStatusCode.ServiceUnavailable, "the current operation mode conflicts with the given request: reading is not permitted\n", ErrorCodes.StoreReadOnly },
    };

    public static TheoryData<string, HttpStatusCode, string> UnmappedErrors => new()
    {
        // A conflict with any other text does not match, not even one that starts alike.
        { "write-events", HttpStatusCode.Conflict, "state conflict: the store is busy\n" },
        { "write-events", HttpStatusCode.Conflict, "schema conflict: something else\n" },
        { "write-events", HttpStatusCode.Conflict, "conflict\n" },
        { "register-event-schema", HttpStatusCode.Conflict, "schema conflict: something else\n" },
        { "read-events", HttpStatusCode.Conflict, "state conflict: precondition failed\n" },
        // A text matches only for the call that sends it.
        { "register-event-schema", HttpStatusCode.Conflict, "state conflict: precondition failed\n" },
        { "register-event-schema", HttpStatusCode.Conflict, "schema conflict: event candidate does not match schema: expected string, but got number\n" },
        { "write-events", HttpStatusCode.Conflict, "schema conflict: schema already exists\n" },
        { "write-events", HttpStatusCode.Conflict, "schema conflict: event with ID 0: expected string, but got number\n" },
        { "write-events", HttpStatusCode.Conflict, "state conflict: fromLatestEvent results in an event ID greater than upperBound ID\n" },
        { "observe-events", HttpStatusCode.Conflict, "state conflict: fromLatestEvent results in an event ID greater than upperBound ID\n" },
        // A status of 404 only names an unknown event type when reading one.
        { "read-events", HttpStatusCode.NotFound, "not found\n" },
        { "write-events", HttpStatusCode.NotFound, "not found\n" },
        // A status of 507 only names an invalid license when writing events.
        { "register-event-schema", HttpStatusCode.InsufficientStorage, "insufficient storage\n" },
        // A status of 503 only names a store in read-only mode with the text of the operation mode.
        { "write-events", HttpStatusCode.ServiceUnavailable, "service unavailable\n" },
        { "read-events", HttpStatusCode.ServiceUnavailable, "the server is shutting down\n" },
        // Everything else keeps no code.
        { "write-events", HttpStatusCode.BadRequest, "malformed subject\n" },
        { "read-event-type", HttpStatusCode.BadRequest, "invalid event type: 'io.eventsourcingdb.'\n" },
        { "verify-api-token", HttpStatusCode.Unauthorized, "unauthorized\n" },
        { "read-events", HttpStatusCode.InternalServerError, "\n" },
    };

    [Theory]
    [MemberData(nameof(MappedErrors))]
    public async Task AssignsTheCodeOfTheV2ApiToAnErrorOfTheV1ApiThatClearlyMatchesIt(
        string request,
        HttpStatusCode status,
        string text,
        string expectedCode)
    {
        var exception = await RunAgainstV1Async(request, status, text);

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(status, exception.Status);
        Assert.Equal(text.TrimEnd('\n'), exception.Description);
        Assert.Null(exception.Details);
    }

    [Theory]
    [MemberData(nameof(UnmappedErrors))]
    public async Task AssignsNoCodeToAnyOtherErrorOfTheV1Api(string request, HttpStatusCode status, string text)
    {
        var exception = await RunAgainstV1Async(request, status, text);

        Assert.Null(exception.Code);
        Assert.Equal(status, exception.Status);
        Assert.Equal(text.TrimEnd('\n'), exception.Description);
    }

    private static async Task<DbApiException> RunAgainstV1Async(string request, HttpStatusCode status, string text)
    {
        var server = FakeServer.ForApi("v1", _ => FakeServer.Text(status, text));
        var client = server.CreateClient();

        var exception = await Record.ExceptionAsync(() => TestRequests.RunAsync(client, request, TestContext.Current.CancellationToken));

        Assert.StartsWith("/api/v1/", server.Requests[^1].Path, StringComparison.Ordinal);

        return Assert.IsType<DbApiException>(exception);
    }
}
