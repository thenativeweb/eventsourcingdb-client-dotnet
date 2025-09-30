using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;

namespace EventSourcingDb;

public interface IClient
{
    /// <summary>
    /// Pings the server to check the connection.
    /// </summary>
    Task PingAsync(CancellationToken token = default);

    /// <summary>
    /// Verifies the API token.
    /// </summary>
    Task VerifyApiTokenAsync(CancellationToken token = default);

    /// <summary>
    /// Writes one or more events to the event store. According to the preconditions, either all or none of the events are written.
    /// </summary>
    Task<IReadOnlyList<Event>> WriteEventsAsync(
        IEnumerable<EventCandidate> events,
        IEnumerable<Precondition>? preconditions = null,
        CancellationToken token = default
    );

    /// <summary>
    /// Reads events for a given subject. The enumeration ends when all events are read.
    /// </summary>
    IAsyncEnumerable<Event> ReadEventsAsync(
        string subject,
        ReadEventsOptions options,
        CancellationToken token = default
    );

    /// <summary>
    /// Observes events for a given subject. The enumeration continues as new events are written to the store.
    /// </summary>
    IAsyncEnumerable<Event> ObserveEventsAsync(
        string subject,
        ObserveEventsOptions options,
        CancellationToken token = default
    );

    /// <summary>
    /// Reads all available event types.
    /// </summary>
    IAsyncEnumerable<EventType> ReadEventTypesAsync(
        CancellationToken token = default
    );

    /// <summary>
    /// Reads a specific event type.
    /// </summary>
    Task<EventType> ReadEventTypeAsync(
        string eventType,
        CancellationToken token = default
    );

    /// <summary>
    /// Runs an EventQL query and returns rows according to the projection from the query.
    /// </summary>
    IAsyncEnumerable<TRow?> RunEventQlQueryAsync<TRow>(
        string query,
        CancellationToken token = default
    );
}
