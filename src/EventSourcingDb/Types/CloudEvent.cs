using System;

namespace EventSourcingDb.Types;

internal record CloudEvent(
    string SpecVersion,
    string Id,
    string Time,
    string Source,
    string Subject,
    string Type,
    string DataContentType,
    object Data,
    string Hash,
    string PredecessorHash,
    string? TraceParent = null,
    string? TraceState = null
)
{
    internal Event ToEvent() =>
        new Event(
            SpecVersion,
            Id,
            DateTimeOffset.Parse(Time),
            Source,
            Subject,
            Type,
            DataContentType,
            Data,
            Hash,
            PredecessorHash,
            TraceParent,
            TraceState
        );
}
