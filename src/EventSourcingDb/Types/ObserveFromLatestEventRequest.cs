namespace EventSourcingDb.Types;

internal record ObserveFromLatestEventRequest(string Subject, string Type, string IfEventIsMissing);
