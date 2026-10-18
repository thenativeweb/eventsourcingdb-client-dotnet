using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Microsoft.Extensions.Logging;

namespace EventSourcingDb;

// Talks to a server through the v2 API, which EventSourcingDB offers since 1.3. Its answers are
// envelopes of a type and data, and its streams end with an end item, except for observing,
// which never ends. Reads use the HTTP method QUERY.
internal sealed class V2Transport(ServerConnection connection) : IClient
{
    private static readonly HttpMethod _query = new HttpMethod("QUERY");
    private static readonly JsonSerializerOptions _defaultSerializerOptions = ServerConnection.DefaultSerializerOptions;

    private JsonSerializerOptions DataSerializerOptions => connection.DataSerializerOptions;
    private ILogger<Client> Logger => connection.Logger;

    public async Task PingAsync(CancellationToken token = default)
    {
        const string expectedEventType = "io.eventsourcingdb.api.ping-received";

        const string pingUrl = "/api/v2/system/ping";
        Logger.LogTrace("Trying to ping '{Url}'...", pingUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pingUrl);
            var envelope = await ReadSingleAsync(request, token).ConfigureAwait(false);

            ThrowIfNotExpectedEvent(envelope, expectedEventType);

            Logger.LogTrace("Pinged '{Url}' successfully.", pingUrl);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to ping '{Url}'.", pingUrl);
            throw;
        }
    }

    public async Task VerifyApiTokenAsync(CancellationToken token = default)
    {
        const string expectedEventType = "io.eventsourcingdb.api.token-verified";

        const string verifyUrl = "/api/v2/system/verify-token";
        Logger.LogTrace("Trying to verify API token using url '{Url}'...", verifyUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, verifyUrl);
            var envelope = await ReadSingleAsync(request, token).ConfigureAwait(false);

            ThrowIfNotExpectedEvent(envelope, expectedEventType);

            Logger.LogTrace("Verified API token using url '{Url}' successfully.", verifyUrl);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to verify API token using url '{Url}'.", verifyUrl);
            throw;
        }
    }

    public async Task<IReadOnlyList<Event>> WriteEventsAsync(
        IEnumerable<EventCandidate> events,
        IEnumerable<Precondition>? preconditions = null,
        CancellationToken token = default)
    {
        preconditions ??= [];
        const string writeEventsUrl = "/api/v2/data/write-events";

        Logger.LogTrace("Trying to write events using url '{Url}'...", writeEventsUrl);

        try
        {
            var candidatesWithSerializedData = events
                .Select(e => e with { Data = JsonSerializer.SerializeToElement(e.Data, DataSerializerOptions) })
                .ToArray();

            var result = await WriteOutcome.GuardAsync(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, writeEventsUrl);
                request.Content = ServerConnection.CreateJsonContent(new { events = candidatesWithSerializedData, preconditions });

                var writtenEvents = new List<Event>();
                await foreach (var item in ReadItemsAsync(request, isFinite: true, token).ConfigureAwait(false))
                {
                    switch (item.Type)
                    {
                        case "event":
                            writtenEvents.Add(ToEvent(item));
                            break;
                        default:
                            throw new Exception($"Failed to handle unsupported item type '{item.Type}'.");
                    }
                }

                return writtenEvents;
            }, IsRefusal, token).ConfigureAwait(false);

            Logger.LogTrace("Written '{Count}' events using url '{Url}' successfully.", result.Count, writeEventsUrl);

            return result;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to write events using url '{Url}'.", writeEventsUrl);
            throw;
        }
    }

    public async IAsyncEnumerable<Event> ReadEventsAsync(
        string subject,
        ReadEventsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        const string readEventsUrl = "/api/v2/data/read-events";

        Logger.LogTrace("Trying to read events using url '{Url}'...", readEventsUrl);

        var requestOptions = new ReadEventsRequestOptions(options);

        using var request = new HttpRequestMessage(_query, readEventsUrl);
        request.Content = ServerConnection.CreateJsonContent(new { subject, Options = requestOptions });

        await foreach (var item in ReadItemsAsync(request, isFinite: true, token).ConfigureAwait(false))
        {
            switch (item.Type)
            {
                case "event":
                    yield return ToEvent(item);
                    break;
                default:
                    throw new Exception($"Failed to handle unsupported item type '{item.Type}'.");
            }
        }
    }

    public async IAsyncEnumerable<string> ReadSubjectsAsync(
        string subject,
        ReadSubjectsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        const string readSubjectsUrl = "/api/v2/data/read-subjects";

        Logger.LogTrace("Trying to read subjects using url '{Url}'...", readSubjectsUrl);

        using var request = new HttpRequestMessage(_query, readSubjectsUrl);
        request.Content = ServerConnection.CreateJsonContent(new { subject, options = new { recursive = options.Recursive } });

        await foreach (var item in ReadItemsAsync(request, isFinite: true, token).ConfigureAwait(false))
        {
            switch (item.Type)
            {
                case "subject":
                    item.ThrowIfNotExpectedData("subject");
                    var readSubject = item.Data.Deserialize<Subject>(_defaultSerializerOptions);
                    if (readSubject is null)
                    {
                        throw new InvalidValueException("Failed to deserialize stream subject.");
                    }

                    yield return readSubject.Name;
                    break;
                default:
                    throw new Exception($"Failed to handle unsupported item type '{item.Type}'.");
            }
        }
    }

    public async IAsyncEnumerable<Event> ObserveEventsAsync(
        string subject,
        ObserveEventsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        const string observeEventsUrl = "/api/v2/data/observe-events";

        Logger.LogTrace("Trying to observe events using url '{Url}'...", observeEventsUrl);

        var requestOptions = new ObserveEventsRequestOptions(options);

        using var request = new HttpRequestMessage(_query, observeEventsUrl);
        request.Content = ServerConnection.CreateJsonContent(new { subject, Options = requestOptions });

        await foreach (var item in ReadItemsAsync(request, isFinite: false, token).ConfigureAwait(false))
        {
            switch (item.Type)
            {
                case "event":
                    yield return ToEvent(item);
                    break;
                default:
                    throw new Exception($"Failed to handle unsupported item type '{item.Type}'.");
            }
        }
    }

    public async IAsyncEnumerable<EventType> ReadEventTypesAsync(
        [EnumeratorCancellation] CancellationToken token = default)
    {
        const string readEventTypesUrl = "/api/v2/data/read-event-types";

        Logger.LogTrace("Trying to read event types using url '{Url}'...", readEventTypesUrl);

        using var request = new HttpRequestMessage(_query, readEventTypesUrl);

        await foreach (var item in ReadItemsAsync(request, isFinite: true, token).ConfigureAwait(false))
        {
            switch (item.Type)
            {
                case "eventType":
                    yield return ToEventType(item);
                    break;
                default:
                    throw new Exception($"Failed to handle unsupported item type '{item.Type}'.");
            }
        }
    }

    public async Task<EventType> ReadEventTypeAsync(
        string eventType,
        CancellationToken token = default)
    {
        const string readEventTypeUrl = "/api/v2/data/read-event-type";

        Logger.LogTrace("Trying to read event type using url '{Url}'...", readEventTypeUrl);

        using var request = new HttpRequestMessage(_query, readEventTypeUrl);
        request.Content = ServerConnection.CreateJsonContent(new { type = eventType });

        var envelope = await ReadSingleAsync(request, token).ConfigureAwait(false);

        if (envelope.Type != "eventType")
        {
            throw new InvalidValueException($"Failed to get the expected response, got '{envelope.Type}' expected 'eventType'.");
        }

        return ToEventType(envelope);
    }

    public async Task RegisterEventSchemaAsync(
        string eventType,
        JsonElement schema,
        CancellationToken token = default)
    {
        const string registerEventSchemaUrl = "/api/v2/data/register-event-schema";

        Logger.LogTrace("Trying to register event schema using url '{Url}'...", registerEventSchemaUrl);

        await WriteOutcome.GuardAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, registerEventSchemaUrl);
            request.Content = ServerConnection.CreateJsonContent(new { type = eventType, schema });

            await foreach (var item in ReadItemsAsync(request, isFinite: true, token).ConfigureAwait(false))
            {
                switch (item.Type)
                {
                    case "event":
                        // The event tells which schema was registered, which is the one that was sent.
                        break;
                    default:
                        throw new Exception($"Failed to handle unsupported item type '{item.Type}'.");
                }
            }

            return true;
        }, IsRefusal, token).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TRow?> RunEventQlQueryAsync<TRow>(
        string query,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        const string runEventQlQueryUrl = "/api/v2/data/run-eventql-query";

        Logger.LogTrace("Trying to run EventQL query using url '{Url}'...", runEventQlQueryUrl);

        using var request = new HttpRequestMessage(_query, runEventQlQueryUrl);
        request.Content = ServerConnection.CreateJsonContent(new RequestBody(query));

        await foreach (var item in ReadItemsAsync(request, isFinite: true, token).ConfigureAwait(false))
        {
            switch (item.Type)
            {
                case "row":
                    item.ThrowIfNotExpectedData("row");
                    var row = item.Data.Deserialize<RowData>(_defaultSerializerOptions);
                    if (row is null)
                    {
                        throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{item.Data}' into row.");
                    }

                    yield return Rows.Deserialize<TRow>(row.Value, DataSerializerOptions);
                    break;
                default:
                    throw new Exception($"Failed to handle unsupported item type '{item.Type}'.");
            }
        }
    }

    // Sends a request whose answer is a single envelope, and returns it. An error in the answer
    // fails with a DbApiException.
    private async Task<Envelope> ReadSingleAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var response = await connection.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
        await ThrowIfNotOkAsync(response, token).ConfigureAwait(false);

        var rawEnvelope = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var envelope = JsonSerializer
            .Deserialize<Envelope>(rawEnvelope, _defaultSerializerOptions)
            .ThrowIfNull(rawEnvelope);

        if (envelope.Type == "error")
        {
            throw CreateDomainError(envelope);
        }

        return envelope;
    }

    // Sends a request whose answer is a stream, and reads its items. Heartbeats are skipped, and
    // an error ends the stream with a DbApiException. A finite stream ends with an end item, so
    // one that breaks off before it fails.
    private async IAsyncEnumerable<Envelope> ReadItemsAsync(
        HttpRequestMessage request,
        bool isFinite,
        [EnumeratorCancellation] CancellationToken token)
    {
        using var response = await connection.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await ThrowIfNotOkAsync(response, token).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var lineReader = new StreamLineReader(stream);

        while (true)
        {
            var rawItem = await lineReader.ReadLineAsync(token).ConfigureAwait(false);

            if (rawItem is null)
            {
                if (isFinite)
                {
                    throw new HttpIOException(HttpRequestError.ResponseEnded, "The stream ended before its end item.");
                }

                yield break;
            }

            var item = JsonSerializer
                .Deserialize<Envelope>(rawItem, _defaultSerializerOptions)
                .ThrowIfNull(rawItem);

            switch (item.Type)
            {
                case "heartbeat":
                    Logger.LogTrace("Heartbeat received.");
                    break;
                case "error":
                    throw CreateDomainError(item);
                case "end":
                    yield break;
                default:
                    yield return item;
                    break;
            }
        }
    }

    private static void ThrowIfNotExpectedEvent(Envelope envelope, string expectedEventType)
    {
        if (envelope.Type != "event")
        {
            throw new InvalidValueException($"Failed to get the expected response, got '{envelope.Type}' expected 'event'.");
        }

        envelope.ThrowIfNotExpectedData("event");
        envelope.Data.Deserialize<Response>(_defaultSerializerOptions).ThrowNotExpectedType(expectedEventType);
    }

    private Event ToEvent(Envelope item)
    {
        item.ThrowIfNotExpectedData("event");
        var cloudEvent = item.Data.Deserialize<CloudEvent>(_defaultSerializerOptions);
        if (cloudEvent is null)
        {
            throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{item.Data}' into cloud event.");
        }

        return new Event(cloudEvent, DataSerializerOptions);
    }

    private static EventType ToEventType(Envelope item)
    {
        item.ThrowIfNotExpectedData("eventType");
        var eventType = item.Data.Deserialize<EventType>(_defaultSerializerOptions);
        if (eventType is null)
        {
            throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{item.Data}' into event type.");
        }

        return eventType;
    }

    private static DbApiException CreateDomainError(Envelope envelope)
    {
        var error = envelope.Data.ValueKind is JsonValueKind.Object
            ? envelope.Data.Deserialize<ErrorData>(_defaultSerializerOptions)
            : null;

        if (error is null)
        {
            throw new InvalidValueException($"Received an item of type 'error', but its data holds no error: '{envelope.Data}'.");
        }

        return new DbApiException(HttpStatusCode.OK, error.Description ?? string.Empty, error.Code, error.Details);
    }

    // The v2 API answers every request that it accepts with 200 and reports what goes wrong
    // within its answer. Any other status refuses the request, with a text as the description.
    private static async Task ThrowIfNotOkAsync(HttpResponseMessage response, CancellationToken token)
    {
        response.ThrowIfNotValidServerHeader();

        if (response.StatusCode is HttpStatusCode.OK)
        {
            return;
        }

        throw await ServerConnection
            .ReadStatusErrorAsync(response, (_, _) => null, token)
            .ConfigureAwait(false);
    }

    // The v2 API refuses a write that it does not store with any error within its answer except
    // store-failed, or with a status of 400, 401, 408, 413, 429, or 503.
    private static bool IsRefusal(DbApiException exception)
    {
        if (exception.Status is HttpStatusCode.OK)
        {
            return exception.Code != ErrorCodes.StoreFailed;
        }

        return exception.Status is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.RequestEntityTooLarge
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.ServiceUnavailable;
    }
}
