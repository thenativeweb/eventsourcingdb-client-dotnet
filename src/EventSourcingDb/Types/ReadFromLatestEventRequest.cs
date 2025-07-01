namespace EventSourcingDb.Types;

internal record ReadFromLatestEventRequest(string Subject, string Type, string IfEventIsMissing);
