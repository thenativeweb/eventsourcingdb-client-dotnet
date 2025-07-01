using System;

namespace EventSourcingDb.Types;

internal class ReadEventsRequestOptions
{
    internal ReadEventsRequestOptions(ReadEventsOptions options)
    {
        Recursive = options.Recursive;
        Order = options.Order != null
            ? options.Order switch
            {
                Types.Order.AntiChronological => "antichronological",
                Types.Order.Chronological => "chronological",
                _ => throw new Exception($"Unhandled switch case {options.Order}.")
            }
            : null;
        LowerBound = options.LowerBound != null
            ? new BoundRequest(options.LowerBound.Id, options.LowerBound.Type.ToString().ToLowerInvariant())
            : null;
        UpperBound = options.UpperBound != null
            ? new BoundRequest(options.UpperBound.Id, options.UpperBound.Type.ToString().ToLowerInvariant())
            : null;
        FromLatestEvent = options.FromLatestEvent != null
            ? new ReadFromLatestEventRequest(
                options.FromLatestEvent.Subject,
                options.FromLatestEvent.Type,
                options.FromLatestEvent.IfEventIsMissing switch
                {
                    ReadIfEventIsMissing.ReadEverything => "read-everything",
                    ReadIfEventIsMissing.ReadNothing => "read-nothing",
                    _ => throw new Exception($"Unhandled switch case {options.FromLatestEvent.IfEventIsMissing}.")
                }
            )
            : null;
    }

    public bool Recursive { get; init; }

    public string? Order { get; set; }

    public BoundRequest? LowerBound { get; init; }

    public BoundRequest? UpperBound { get; init; }

    public ReadFromLatestEventRequest? FromLatestEvent { get; init; }
}
