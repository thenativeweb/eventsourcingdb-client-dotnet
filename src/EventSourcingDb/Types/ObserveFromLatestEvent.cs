namespace EventSourcingDb.Types;

public record ObserveFromLatestEvent(string Subject, string Type, ObserveIfEventIsMissing IfEventIsMissing);
