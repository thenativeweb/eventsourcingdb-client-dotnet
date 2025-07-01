namespace EventSourcingDb.Types;

public record ReadFromLatestEvent(string Subject, string Type, ReadIfEventIsMissing IfEventIsMissing);
