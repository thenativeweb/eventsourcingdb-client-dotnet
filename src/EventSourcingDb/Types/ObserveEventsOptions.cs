namespace EventSourcingDb.Types;

public record ObserveEventsOptions(
    bool Recursive,
    Bound? LowerBound = null,
    ObserveFromLatestEvent? FromLatestEvent = null
);
