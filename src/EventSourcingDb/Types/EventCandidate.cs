namespace EventSourcingDb.Types;

public record EventCandidate(
    string Source,
    string Subject,
    string Type,
    object Data,
    string? TraceParent = null,
    string? TraceState = null
);
