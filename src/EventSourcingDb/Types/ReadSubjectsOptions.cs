namespace EventSourcingDb.Types;

/// <summary>
/// The options for reading subjects. With <see cref="Recursive"/> set, the subject is read together
/// with all subjects below it, otherwise the subject alone.
/// </summary>
public record ReadSubjectsOptions(
    bool Recursive
);
