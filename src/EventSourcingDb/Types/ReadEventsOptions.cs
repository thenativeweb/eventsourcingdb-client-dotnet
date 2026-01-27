namespace EventSourcingDb.Types;

public record ReadEventsOptions(
    bool Recursive,
    Order? Order = null,
    Bound? LowerBound = null,
    Bound? UpperBound = null,
    ReadFromLatestEvent? FromLatestEvent = null
);
