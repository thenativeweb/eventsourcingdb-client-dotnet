using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Microsoft.Extensions.Logging;

namespace EventSourcingDb;

// Talks to a server through the v1 API, which every server before EventSourcingDB 1.3 offers.
// It is used only where the server does not offer the v2 API, and goes away together with the
// support for these servers.
internal sealed class V1Transport(ServerConnection connection) : IClient
{
    private static readonly JsonSerializerOptions _defaultSerializerOptions = ServerConnection.DefaultSerializerOptions;

    private JsonSerializerOptions DataSerializerOptions => connection.DataSerializerOptions;
    private ILogger<Client> Logger => connection.Logger;

    public async Task PingAsync(CancellationToken token = default)
    {
        const string expectedEventType = "io.eventsourcingdb.api.ping-received";

        const string pingUrl = "/api/v1/ping";
        Logger.LogTrace("Trying to ping '{Url}'...", pingUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pingUrl);
            using var response = await connection.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            await ThrowIfNotOkAsync(response, V1Request.Ping, token).ConfigureAwait(false);

            var pingResponse = await response.Content
                .ReadFromJsonAsync<Response>(_defaultSerializerOptions, token)
                .ConfigureAwait(false);

            pingResponse.ThrowNotExpectedType(expectedEventType);

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
        const string expectedEventType = "io.eventsourcingdb.api.api-token-verified";

        const string verifyUrl = "/api/v1/verify-api-token";
        Logger.LogTrace("Trying to verify API token using url '{Url}'...", verifyUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, verifyUrl);
            using var response = await connection.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            await ThrowIfNotOkAsync(response, V1Request.VerifyApiToken, token).ConfigureAwait(false);

            var verifyResponse = await response.Content
                .ReadFromJsonAsync<Response>(_defaultSerializerOptions, token)
                .ConfigureAwait(false);

            verifyResponse.ThrowNotExpectedType(expectedEventType);

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
        const string writeEventsUrl = "/api/v1/write-events";

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

                using var response = await connection.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
                await ThrowIfNotOkAsync(response, V1Request.WriteEvents, token).ConfigureAwait(false);

                var eventsResponse = await response.Content
                    .ReadFromJsonAsync<CloudEvent[]>(_defaultSerializerOptions, token)
                    .ConfigureAwait(false);

                if (eventsResponse is null)
                {
                    throw new InvalidValueException("Failed to parse response.");
                }

                return eventsResponse
                    .Select(cloudEvent => new Event(cloudEvent, DataSerializerOptions))
                    .ToArray();
            }, IsRefusal, token).ConfigureAwait(false);

            Logger.LogTrace("Written '{Count}' events using url '{Url}' successfully.", result.Length, writeEventsUrl);

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
        const string readEventsUrl = "/api/v1/read-events";

        Logger.LogTrace("Trying to read events using url '{Url}'...", readEventsUrl);

        var requestOptions = new ReadEventsRequestOptions(options);

        using var request = new HttpRequestMessage(HttpMethod.Post, readEventsUrl);
        request.Content = ServerConnection.CreateJsonContent(new { subject, Options = requestOptions });

        await foreach (var line in ReadLinesAsync(request, V1Request.ReadEvents, token).ConfigureAwait(false))
        {
            switch (line.Type)
            {
                case "event":
                    yield return ToEvent(line);
                    break;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }
        }
    }

    public async IAsyncEnumerable<string> ReadSubjectsAsync(
        string subject,
        ReadSubjectsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        const string readSubjectsUrl = "/api/v1/read-subjects";

        Logger.LogTrace("Trying to read subjects using url '{Url}'...", readSubjectsUrl);

        // The v1 API always reads the subjects below the base subject as well, so a read that is
        // not recursive keeps only the subject itself.
        using var request = new HttpRequestMessage(HttpMethod.Post, readSubjectsUrl);
        request.Content = ServerConnection.CreateJsonContent(new { baseSubject = subject });

        await foreach (var line in ReadLinesAsync(request, V1Request.ReadSubjects, token).ConfigureAwait(false))
        {
            switch (line.Type)
            {
                case "subject":
                    line.ThrowIfNotExpectedPayload("subject");
                    var readSubject = line.Payload.Deserialize<Subject>(_defaultSerializerOptions);
                    if (readSubject is null)
                    {
                        throw new InvalidValueException("Failed to deserialize stream subject.");
                    }

                    if (options.Recursive)
                    {
                        yield return readSubject.Name;
                        break;
                    }

                    if (readSubject.Name == subject)
                    {
                        yield return readSubject.Name;

                        // A subject appears only once, so nothing else is left to keep.
                        yield break;
                    }

                    break;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }
        }
    }

    public async IAsyncEnumerable<Event> ObserveEventsAsync(
        string subject,
        ObserveEventsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        const string observeEventsUrl = "/api/v1/observe-events";

        Logger.LogTrace("Trying to observe events using url '{Url}'...", observeEventsUrl);

        var requestOptions = new ObserveEventsRequestOptions(options);

        using var request = new HttpRequestMessage(HttpMethod.Post, observeEventsUrl);
        request.Content = ServerConnection.CreateJsonContent(new { subject, Options = requestOptions });

        await foreach (var line in ReadLinesAsync(request, V1Request.ObserveEvents, token, isObservation: true).ConfigureAwait(false))
        {
            switch (line.Type)
            {
                case "event":
                    yield return ToEvent(line);
                    break;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }
        }
    }

    public async IAsyncEnumerable<EventType> ReadEventTypesAsync(
        [EnumeratorCancellation] CancellationToken token = default)
    {
        const string readEventTypesUrl = "/api/v1/read-event-types";

        Logger.LogTrace("Trying to read event types using url '{Url}'...", readEventTypesUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, readEventTypesUrl);

        await foreach (var line in ReadLinesAsync(request, V1Request.ReadEventTypes, token).ConfigureAwait(false))
        {
            switch (line.Type)
            {
                case "eventType":
                    line.ThrowIfNotExpectedPayload("eventType");
                    var eventType = line.Payload.Deserialize<V1EventType>(_defaultSerializerOptions);
                    if (eventType is null)
                    {
                        throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{line.Payload}' into event type.");
                    }

                    yield return eventType.ToEventType();
                    break;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }
        }
    }

    public async Task<EventType> ReadEventTypeAsync(
        string eventType,
        CancellationToken token = default)
    {
        const string readEventTypeUrl = "/api/v1/read-event-type";

        Logger.LogTrace("Trying to read event type using url '{Url}'...", readEventTypeUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, readEventTypeUrl);
        request.Content = ServerConnection.CreateJsonContent(new { eventType });

        using var response = await connection.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
        await ThrowIfNotOkAsync(response, V1Request.ReadEventType, token).ConfigureAwait(false);

        var eventTypeResponse = await response.Content
            .ReadFromJsonAsync<V1EventType>(_defaultSerializerOptions, token)
            .ConfigureAwait(false);

        if (eventTypeResponse is null)
        {
            throw new InvalidValueException($"Failed to get the expected response, got null for event type '{eventType}'.");
        }

        return eventTypeResponse.ToEventType();
    }

    public async Task RegisterEventSchemaAsync(
        string eventType,
        JsonElement schema,
        CancellationToken token = default)
    {
        const string registerEventSchemaUrl = "/api/v1/register-event-schema";

        Logger.LogTrace("Trying to register event schema using url '{Url}'...", registerEventSchemaUrl);

        await WriteOutcome.GuardAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, registerEventSchemaUrl);
            request.Content = ServerConnection.CreateJsonContent(new { eventType, schema });

            using var response = await connection.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            await ThrowIfNotOkAsync(response, V1Request.RegisterEventSchema, token).ConfigureAwait(false);

            return true;
        }, IsRefusal, token).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TRow?> RunEventQlQueryAsync<TRow>(
        string query,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        const string runEventQlQueryUrl = "/api/v1/run-eventql-query";

        Logger.LogTrace("Trying to run EventQL query using url '{Url}'...", runEventQlQueryUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, runEventQlQueryUrl);
        request.Content = ServerConnection.CreateJsonContent(new RequestBody(query));

        await foreach (var line in ReadLinesAsync(request, V1Request.RunEventQlQuery, token).ConfigureAwait(false))
        {
            switch (line.Type)
            {
                case "row":
                    yield return Rows.Deserialize<TRow>(line.Payload, DataSerializerOptions);
                    break;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }
        }
    }

    // Sends a request whose answer is a stream, and reads its lines. Heartbeats are skipped, and
    // an error ends the stream with a DbApiException. The streams of the v1 API have no end item,
    // so they end when the server closes them, except for an observation, which never ends on its
    // own, and fails if it does.
    private async IAsyncEnumerable<Line> ReadLinesAsync(
        HttpRequestMessage request,
        V1Request v1Request,
        [EnumeratorCancellation] CancellationToken token,
        bool isObservation = false)
    {
        using var response = await connection.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await ThrowIfNotOkAsync(response, v1Request, token).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var lineReader = new StreamLineReader(stream);

        while (true)
        {
            var rawLine = await lineReader.ReadLineAsync(token).ConfigureAwait(false);

            if (rawLine is null)
            {
                if (isObservation)
                {
                    throw ServerConnection.CreateStreamEndedPrematurelyException();
                }

                yield break;
            }

            var line = JsonSerializer
                .Deserialize<Line>(rawLine, _defaultSerializerOptions)
                .ThrowIfNull(rawLine);

            switch (line.Type)
            {
                case "heartbeat":
                    Logger.LogTrace("Heartbeat received.");
                    break;
                case "error":
                    throw new DbApiException(response.StatusCode, line.GetErrorDescription());
                default:
                    yield return line;
                    break;
            }
        }
    }

    private Event ToEvent(Line line)
    {
        line.ThrowIfNotExpectedPayload("event");
        var cloudEvent = line.Payload.Deserialize<CloudEvent>(_defaultSerializerOptions);
        if (cloudEvent is null)
        {
            throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{line.Payload}' into cloud event.");
        }

        return new Event(cloudEvent, DataSerializerOptions);
    }

    private static async Task ThrowIfNotOkAsync(HttpResponseMessage response, V1Request v1Request, CancellationToken token)
    {
        response.ThrowIfNotValidServerHeader();

        if (response.StatusCode is HttpStatusCode.OK)
        {
            return;
        }

        throw await ServerConnection
            .ReadStatusErrorAsync(response, (status, description) => V1Errors.GetCode(v1Request, status, description), token)
            .ConfigureAwait(false);
    }

    // The v1 API refuses a write that it does not store with a status of 4xx, 503, or 507.
    private static bool IsRefusal(DbApiException exception)
    {
        var status = (int)exception.Status;

        return status is >= 400 and < 500 or 503 or 507;
    }
}
