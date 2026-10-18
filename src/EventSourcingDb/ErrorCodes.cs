namespace EventSourcingDb;

/// <summary>
/// The codes of the errors that EventSourcingDB reports, as <see cref="DbApiException.Code"/> holds
/// them. Each code is the URL of the page that documents the error.
/// </summary>
public static class ErrorCodes
{
    public const string SubjectMissing = "https://docs.eventsourcingdb.io/errors/subject-missing";
    public const string SubjectMalformed = "https://docs.eventsourcingdb.io/errors/subject-malformed";
    public const string OrderUnknown = "https://docs.eventsourcingdb.io/errors/order-unknown";
    public const string EventIdMissing = "https://docs.eventsourcingdb.io/errors/event-id-missing";
    public const string EventIdMalformed = "https://docs.eventsourcingdb.io/errors/event-id-malformed";
    public const string BoundTypeMissing = "https://docs.eventsourcingdb.io/errors/bound-type-missing";
    public const string BoundTypeUnknown = "https://docs.eventsourcingdb.io/errors/bound-type-unknown";
    public const string RangeUnsatisfiable = "https://docs.eventsourcingdb.io/errors/range-unsatisfiable";
    public const string FromLatestEventIncomplete = "https://docs.eventsourcingdb.io/errors/from-latest-event-incomplete";
    public const string IfEventIsMissingUnknown = "https://docs.eventsourcingdb.io/errors/if-event-is-missing-unknown";
    public const string SelectionConflicting = "https://docs.eventsourcingdb.io/errors/selection-conflicting";
    public const string EventTypeMissing = "https://docs.eventsourcingdb.io/errors/event-type-missing";
    public const string EventTypeMalformed = "https://docs.eventsourcingdb.io/errors/event-type-malformed";
    public const string EventTypeUnknown = "https://docs.eventsourcingdb.io/errors/event-type-unknown";
    public const string SourceMissing = "https://docs.eventsourcingdb.io/errors/source-missing";
    public const string SourceMalformed = "https://docs.eventsourcingdb.io/errors/source-malformed";
    public const string DataMissing = "https://docs.eventsourcingdb.io/errors/data-missing";
    public const string TraceparentMalformed = "https://docs.eventsourcingdb.io/errors/traceparent-malformed";
    public const string TracestateMalformed = "https://docs.eventsourcingdb.io/errors/tracestate-malformed";
    public const string TracestateWithoutTraceparent = "https://docs.eventsourcingdb.io/errors/tracestate-without-traceparent";
    public const string EventsMissing = "https://docs.eventsourcingdb.io/errors/events-missing";
    public const string PreconditionTypeMissing = "https://docs.eventsourcingdb.io/errors/precondition-type-missing";
    public const string PreconditionUnknown = "https://docs.eventsourcingdb.io/errors/precondition-unknown";
    public const string PayloadMissing = "https://docs.eventsourcingdb.io/errors/payload-missing";
    public const string LicenseInvalid = "https://docs.eventsourcingdb.io/errors/license-invalid";
    public const string CapacityExceeded = "https://docs.eventsourcingdb.io/errors/capacity-exceeded";
    public const string EventSchemaViolated = "https://docs.eventsourcingdb.io/errors/event-schema-violated";
    public const string EventSchemaUnregistered = "https://docs.eventsourcingdb.io/errors/event-schema-unregistered";
    public const string EventSchemaMissing = "https://docs.eventsourcingdb.io/errors/event-schema-missing";
    public const string EventSchemaMalformed = "https://docs.eventsourcingdb.io/errors/event-schema-malformed";
    public const string EventSchemaNotObject = "https://docs.eventsourcingdb.io/errors/event-schema-not-object";
    public const string EventSchemaAlreadyRegistered = "https://docs.eventsourcingdb.io/errors/event-schema-already-registered";
    public const string LastAuditedEventMissing = "https://docs.eventsourcingdb.io/errors/last-audited-event-missing";
    public const string LastAuditedEventMismatched = "https://docs.eventsourcingdb.io/errors/last-audited-event-mismatched";
    public const string LastAuditedEventUnknown = "https://docs.eventsourcingdb.io/errors/last-audited-event-unknown";
    public const string EventHashMissing = "https://docs.eventsourcingdb.io/errors/event-hash-missing";
    public const string EventHashMalformed = "https://docs.eventsourcingdb.io/errors/event-hash-malformed";
    public const string EventChainBroken = "https://docs.eventsourcingdb.io/errors/event-chain-broken";
    public const string LastBackedUpEventMissing = "https://docs.eventsourcingdb.io/errors/last-backed-up-event-missing";
    public const string LastBackedUpEventMismatched = "https://docs.eventsourcingdb.io/errors/last-backed-up-event-mismatched";
    public const string LastBackedUpEventUnknown = "https://docs.eventsourcingdb.io/errors/last-backed-up-event-unknown";
    public const string StoreNotEmpty = "https://docs.eventsourcingdb.io/errors/store-not-empty";
    public const string EventHashVersionUnknown = "https://docs.eventsourcingdb.io/errors/event-hash-version-unknown";
    public const string BackupMalformed = "https://docs.eventsourcingdb.io/errors/backup-malformed";
    public const string BackupIncomplete = "https://docs.eventsourcingdb.io/errors/backup-incomplete";
    public const string BackupFormatUnknown = "https://docs.eventsourcingdb.io/errors/backup-format-unknown";
    public const string FullBackupMissing = "https://docs.eventsourcingdb.io/errors/full-backup-missing";
    public const string BackupIncrementDisconnected = "https://docs.eventsourcingdb.io/errors/backup-increment-disconnected";
    public const string BackupFailed = "https://docs.eventsourcingdb.io/errors/backup-failed";
    public const string BackupUnreadable = "https://docs.eventsourcingdb.io/errors/backup-unreadable";
    public const string EventTooLarge = "https://docs.eventsourcingdb.io/errors/event-too-large";
    public const string DataMalformed = "https://docs.eventsourcingdb.io/errors/data-malformed";
    public const string QueryMissing = "https://docs.eventsourcingdb.io/errors/query-missing";
    public const string QueryMalformed = "https://docs.eventsourcingdb.io/errors/query-malformed";
    public const string QueryUnevaluable = "https://docs.eventsourcingdb.io/errors/query-unevaluable";
    public const string QueryNotBoolean = "https://docs.eventsourcingdb.io/errors/query-not-boolean";
    public const string PreconditionViolated = "https://docs.eventsourcingdb.io/errors/precondition-violated";
    public const string StoreReadOnly = "https://docs.eventsourcingdb.io/errors/store-read-only";
    public const string ReadOnlyModeAlreadyEnabled = "https://docs.eventsourcingdb.io/errors/read-only-mode-already-enabled";
    public const string DiskFull = "https://docs.eventsourcingdb.io/errors/disk-full";
    public const string RestoreInProgress = "https://docs.eventsourcingdb.io/errors/restore-in-progress";
    public const string StoreFailed = "https://docs.eventsourcingdb.io/errors/store-failed";
    public const string IndexesRebuilding = "https://docs.eventsourcingdb.io/errors/indexes-rebuilding";
    public const string ServerShuttingDown = "https://docs.eventsourcingdb.io/errors/server-shutting-down";
}
