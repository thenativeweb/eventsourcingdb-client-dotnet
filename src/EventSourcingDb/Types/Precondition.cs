namespace EventSourcingDb.Types;

public record Precondition
{
    public required string Type { get; init; }

    public required object Payload { get; init; }

    private Precondition() { }

    public static Precondition IsSubjectPristinePrecondition(string subject) =>
        new Precondition { Type = "isSubjectPristine", Payload = new IsSubjectPristinePrecondition(subject) };

    public static Precondition IsSubjectOnEventIdPrecondition(string subject, string eventId) =>
        new Precondition { Type = "isSubjectOnEventId", Payload = new IsSubjectOnEventIdPrecondition(subject, eventId) };
}
