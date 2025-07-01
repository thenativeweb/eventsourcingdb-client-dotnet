using System.Diagnostics.CodeAnalysis;

namespace EventSourcingDb.Types;

public readonly record struct EventResult
{
    internal EventResult(Event eventResult)
    {
        Event = eventResult;
    }

    internal EventResult(string error)
    {
        Error = error;
    }

    public string? Error { get; }

    public Event? Event { get; }

    [MemberNotNullWhen(true, nameof(Error))]
    [MemberNotNullWhen(false, nameof(Event))]
    public bool IsErrorSet => Error != null;

    [MemberNotNullWhen(true, nameof(Event))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsEventSet => Event != null;
}
