using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;

namespace EventSourcingDb;

/// <summary>
/// A client for EventSourcingDB. Every error that the server reports fails with a
/// <see cref="DbApiException"/>, and every request fails with a
/// <see cref="ConnectionStalledException"/> once nothing arrives from the server for the stall
/// timeout.
/// </summary>
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
    /// If the write fails in a way that leaves open whether the events were stored, it fails with an <see cref="OutcomeUnknownException"/>.
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
    /// Reads the given subject, if it exists, and with <see cref="ReadSubjectsOptions.Recursive"/> set, all subjects below it as well.
    /// </summary>
    IAsyncEnumerable<string> ReadSubjectsAsync(
        string subject,
        ReadSubjectsOptions options,
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
    /// Registers a new event schema for a given event type.
    /// If a schema for the event type already exists, an error is returned.
    /// If the registration fails in a way that leaves open whether the schema was stored, it fails with an <see cref="OutcomeUnknownException"/>.
    /// </summary>
    Task RegisterEventSchemaAsync(
        string eventType,
        JsonElement schema,
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
