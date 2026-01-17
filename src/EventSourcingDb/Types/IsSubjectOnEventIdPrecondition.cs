namespace EventSourcingDb.Types;

internal record IsSubjectOnEventIdPrecondition(string Subject, string EventId);
