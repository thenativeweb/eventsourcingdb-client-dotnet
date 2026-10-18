using System;
using System.Net;

namespace EventSourcingDb;

// The requests of the v1 API, as far as the errors they report differ.
internal enum V1Request
{
    Ping,
    VerifyApiToken,
    WriteEvents,
    RegisterEventSchema,
    ReadEventType,
    ReadEvents,
    ReadSubjects,
    ReadEventTypes,
    ObserveEvents,
    RunEventQlQuery,
}

// The v1 API reports errors as a status and a text, without a code. Those that clearly match an
// error of the v2 API get its code. The texts are those that EventSourcingDB 1.2.0 sends, from
// old/storage at tag v1.2.0, matched from their start. All other errors keep no code.
internal static class V1Errors
{
    private const string PreconditionFailed = "state conflict: precondition failed";
    private const string CapacityExceeded = "state conflict: exceeded max store capacity";
    private const string EventCandidateDoesNotMatchSchema = "schema conflict: event candidate does not match schema";
    private const string SchemaAlreadyExists = "schema conflict: schema already exists";
    private const string SchemaConflictsWithExistingEvent = "schema conflict: event with ID";
    private const string FromLatestEventBeyondUpperBound = "state conflict: fromLatestEvent results in an event ID greater than upperBound ID";
    private const string OperationModeConflicts = "the current operation mode conflicts with the given request";

    public static string? GetCode(V1Request request, HttpStatusCode status, string description)
    {
        bool StartsWith(string text) => description.StartsWith(text, StringComparison.Ordinal);

        return (request, status) switch
        {
            (V1Request.WriteEvents, HttpStatusCode.Conflict) when StartsWith(PreconditionFailed) => ErrorCodes.PreconditionViolated,
            (V1Request.WriteEvents, HttpStatusCode.Conflict) when StartsWith(CapacityExceeded) => ErrorCodes.CapacityExceeded,
            (V1Request.WriteEvents, HttpStatusCode.Conflict) when StartsWith(EventCandidateDoesNotMatchSchema) => ErrorCodes.EventSchemaViolated,
            (V1Request.RegisterEventSchema, HttpStatusCode.Conflict) when StartsWith(SchemaAlreadyExists) => ErrorCodes.EventSchemaAlreadyRegistered,
            (V1Request.RegisterEventSchema, HttpStatusCode.Conflict) when StartsWith(SchemaConflictsWithExistingEvent) => ErrorCodes.EventSchemaViolated,
            (V1Request.ReadEvents, HttpStatusCode.Conflict) when StartsWith(FromLatestEventBeyondUpperBound) => ErrorCodes.RangeUnsatisfiable,
            (V1Request.ReadEventType, HttpStatusCode.NotFound) => ErrorCodes.EventTypeUnknown,
            (_, HttpStatusCode.ServiceUnavailable) when StartsWith(OperationModeConflicts) => ErrorCodes.StoreReadOnly,
            (V1Request.WriteEvents, HttpStatusCode.InsufficientStorage) => ErrorCodes.LicenseInvalid,
            _ => null,
        };
    }
}
