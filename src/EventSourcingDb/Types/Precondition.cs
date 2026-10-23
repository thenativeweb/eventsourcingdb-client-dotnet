using System;

namespace EventSourcingDb.Types;

public record Precondition
{
    public required string Type { get; init; }

    public required object Payload { get; init; }

    private Precondition() { }

    public static Precondition IsSubjectPristinePrecondition(string subject) =>
        new Precondition { Type = "isSubjectPristine", Payload = new IsSubjectPristinePrecondition(subject) };

    public static Precondition IsSubjectPristinePrecondition(string subject, PreconditionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new Precondition { Type = "isSubjectPristine", Payload = new IsSubjectPristinePrecondition(subject, options) };
    }

    public static Precondition IsSubjectPopulatedPrecondition(string subject) =>
        new Precondition { Type = "isSubjectPopulated", Payload = new IsSubjectPopulatedPrecondition(subject) };

    public static Precondition IsSubjectPopulatedPrecondition(string subject, PreconditionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new Precondition { Type = "isSubjectPopulated", Payload = new IsSubjectPopulatedPrecondition(subject, options) };
    }

    public static Precondition IsSubjectOnEventIdPrecondition(string subject, string eventId) =>
        new Precondition { Type = "isSubjectOnEventId", Payload = new IsSubjectOnEventIdPrecondition(subject, eventId) };

    public static Precondition IsSubjectOnEventIdPrecondition(string subject, string eventId, PreconditionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new Precondition { Type = "isSubjectOnEventId", Payload = new IsSubjectOnEventIdPrecondition(subject, eventId, options) };
    }

    public static Precondition IsEventQlQueryTruePrecondition(string query) =>
        new Precondition { Type = "isEventQlQueryTrue", Payload = new IsEventQlQueryTruePrecondition(query) };

    // Whether the precondition applies to the subject together with all subjects below it.
    internal bool IsRecursive => Payload switch
    {
        IsSubjectPristinePrecondition payload => payload.Options?.Recursive ?? false,
        IsSubjectPopulatedPrecondition payload => payload.Options?.Recursive ?? false,
        IsSubjectOnEventIdPrecondition payload => payload.Options?.Recursive ?? false,
        _ => false,
    };

    // The precondition as the v1 API takes it, which knows no options on a precondition. Without
    // them, a precondition checks the subject alone, so this is the same precondition only if it
    // is not recursive.
    internal Precondition WithoutOptions() => Payload switch
    {
        IsSubjectPristinePrecondition payload => this with { Payload = payload with { Options = null } },
        IsSubjectPopulatedPrecondition payload => this with { Payload = payload with { Options = null } },
        IsSubjectOnEventIdPrecondition payload => this with { Payload = payload with { Options = null } },
        _ => this,
    };
}
