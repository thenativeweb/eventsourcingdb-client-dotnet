namespace EventSourcingDb.Types;

internal record IsSubjectPristinePrecondition(string Subject, PreconditionOptions? Options = null);
