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
// error of the v2 API get its code, judged by the texts of EventSourcingDB 1.2.0. All others keep
// no code.
internal static class V1Errors
{
    private const string OperationModeConflicts = "the current operation mode conflicts with the given request";

    public static string? GetCode(V1Request request, HttpStatusCode status, string description)
    {
        return (request, status) switch
        {
            (V1Request.WriteEvents, HttpStatusCode.Conflict) when description.StartsWith("state conflict", StringComparison.Ordinal)
                => ErrorCodes.PreconditionViolated,
            (V1Request.WriteEvents, HttpStatusCode.Conflict) when description.StartsWith("schema conflict", StringComparison.Ordinal)
                => ErrorCodes.EventSchemaViolated,
            (V1Request.RegisterEventSchema, HttpStatusCode.Conflict) when description == "schema conflict: schema already exists"
                => ErrorCodes.EventSchemaAlreadyRegistered,
            (V1Request.ReadEventType, HttpStatusCode.NotFound)
                => ErrorCodes.EventTypeUnknown,
            (_, HttpStatusCode.ServiceUnavailable) when description.StartsWith(OperationModeConflicts, StringComparison.Ordinal)
                => ErrorCodes.StoreReadOnly,
            (V1Request.WriteEvents, HttpStatusCode.InsufficientStorage)
                => ErrorCodes.LicenseInvalid,
            _ => null,
        };
    }
}
