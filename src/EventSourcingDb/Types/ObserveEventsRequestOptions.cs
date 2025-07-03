using System;

namespace EventSourcingDb.Types;

internal class ObserveEventsRequestOptions
{
    internal ObserveEventsRequestOptions(ObserveEventsOptions options)
    {
        Recursive = options.Recursive;
        LowerBound = options.LowerBound != null
            ? new BoundRequest(options.LowerBound.Id, options.LowerBound.Type.ToString().ToLowerInvariant())
            : null;
        FromLatestEvent = options.FromLatestEvent != null
            ? new ObserveFromLatestEventRequest(
                options.FromLatestEvent.Subject,
                options.FromLatestEvent.Type,
                options.FromLatestEvent.IfEventIsMissing switch
                {
                    ObserveIfEventIsMissing.ObserveEverything => "read-everything",
                    ObserveIfEventIsMissing.WaitForEvent=> "wait-for-event",
                    _ => throw new Exception($"Unhandled switch case '{options.FromLatestEvent.IfEventIsMissing}'.")
                }
            )
            : null;
    }

    public bool Recursive { get; init; }

    public BoundRequest? LowerBound { get; init; }

    public ObserveFromLatestEventRequest? FromLatestEvent { get; init; }
}
