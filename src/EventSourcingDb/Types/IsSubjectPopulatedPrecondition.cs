namespace EventSourcingDb.Types;

internal record IsSubjectPopulatedPrecondition(string Subject, PreconditionOptions? Options = null);
