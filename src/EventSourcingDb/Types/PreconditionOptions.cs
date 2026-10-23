namespace EventSourcingDb.Types;

/// <summary>
/// The options for a precondition on a subject. With <see cref="Recursive"/> set, the precondition
/// applies to the subject together with all subjects below it, otherwise to the subject alone.
/// </summary>
public record PreconditionOptions(
    bool Recursive
);
