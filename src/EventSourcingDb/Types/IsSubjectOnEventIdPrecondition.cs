namespace EventSourcingDb.Types;

public record IsSubjectOnEventIdPrecondition(string Subject, string EventId);
