using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventSourcingDb;

public class Client : IClient
{
    private static readonly JsonSerializerOptions _defaultSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly JsonSerializerOptions _dataSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly HttpClient _httpClient = new HttpClient(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }
    );
    private readonly Uri _baseUrl;
    private readonly ILogger<Client> _logger;

    public Client(Uri baseUrl, string apiToken) : this(baseUrl, apiToken, null)
    {
    }

    public Client(Uri baseUrl, string apiToken, ILogger<Client>? logger = null) : this(baseUrl, apiToken, null, logger)
    {
    }

    public Client(Uri baseUrl, string apiToken, JsonSerializerOptions? dataSerializerOptions = null, ILogger<Client>? logger = null)
    {
        _baseUrl = baseUrl;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        if (dataSerializerOptions is not null)
        {
            _dataSerializerOptions = dataSerializerOptions;
        }

        _logger = logger ?? NullLogger<Client>.Instance;
    }

    public async Task PingAsync(CancellationToken token = default)
    {
        const string expectedEventType = "io.eventsourcingdb.api.ping-received";

        var pingUrl = new Uri(_baseUrl, "/api/v1/ping");
        _logger.LogTrace("Trying to ping '{Url}'...", pingUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pingUrl);
            using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            response.ThrowIfNotValidServerHeader();
            await response.ThrowIfNotSuccessStatusCode(token);

            var pingResponse = await response.Content
                .ReadFromJsonAsync<Response>(_defaultSerializerOptions, token)
                .ConfigureAwait(false);

            pingResponse.ThrowNotExpectedType(expectedEventType);

            _logger.LogTrace("Pinged '{Url}' successfully.", pingUrl);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to ping '{Url}'.", pingUrl);
            throw;
        }
    }

    public async Task VerifyApiTokenAsync(CancellationToken token = default)
    {
        const string expectedEventType = "io.eventsourcingdb.api.api-token-verified";

        var verifyUrl = new Uri(_baseUrl, "/api/v1/verify-api-token");
        _logger.LogTrace("Trying to verify API token using url '{Url}'...", verifyUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, verifyUrl);

            using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            response.ThrowIfNotValidServerHeader();
            await response.ThrowIfNotSuccessStatusCode(token);

            var verifyResponse = await response.Content
                .ReadFromJsonAsync<Response>(_defaultSerializerOptions, token)
                .ConfigureAwait(false);

            verifyResponse.ThrowNotExpectedType(expectedEventType);

            _logger.LogTrace("Verified API token using url '{Url}' successfully.", verifyUrl);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to verify API token using url '{Url}'.", verifyUrl);
            throw;
        }
    }

    public async Task<IReadOnlyList<Event>> WriteEventsAsync(
        IEnumerable<EventCandidate> events,
        IEnumerable<Precondition>? preconditions = null,
        CancellationToken token = default)
    {
        preconditions ??= [];
        var writeEventsUrl = new Uri(_baseUrl, "/api/v1/write-events");

        _logger.LogTrace("Trying to write events using url '{Url}'...", writeEventsUrl);

        try
        {
            var candidatesWithSerializedData = events
                .Select(e => e with { Data = JsonSerializer.SerializeToElement(e.Data, _dataSerializerOptions) })
                .ToArray();

            using var request = new HttpRequestMessage(HttpMethod.Post, writeEventsUrl);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { events = candidatesWithSerializedData, preconditions }, _defaultSerializerOptions),
                Encoding.UTF8,
                MediaTypeNames.Application.Json
            );

            using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            response.ThrowIfNotValidServerHeader();
            await response.ThrowIfNotSuccessStatusCode(token);

            var eventsResponse = await response.Content
                .ReadFromJsonAsync<CloudEvent[]>(_defaultSerializerOptions, token)
                .ConfigureAwait(false);

            if (eventsResponse is null)
            {
                throw new InvalidValueException("Failed to parse response.");
            }

            var result = eventsResponse
                .Select(cloudEvent => new Event(cloudEvent, _dataSerializerOptions))
                .ToArray();

            _logger.LogTrace("Written '{Count}' events using url '{Url}' successfully.", result.Length, writeEventsUrl);

            return result;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to write events using url '{Url}'.", writeEventsUrl);
            throw;
        }
    }

    public async IAsyncEnumerable<Event> ReadEventsAsync(
        string subject,
        ReadEventsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var readEventsUrl = new Uri(_baseUrl, "/api/v1/read-events");

        _logger.LogTrace("Trying to read events using url '{Url}'...", readEventsUrl);

        var requestOptions = new ReadEventsRequestOptions(options);

        using var request = new HttpRequestMessage(HttpMethod.Post, readEventsUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { subject, Options = requestOptions }, _defaultSerializerOptions),
            Encoding.UTF8,
            MediaTypeNames.Application.Json
        );

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.ThrowIfNotValidServerHeader();
        await response.ThrowIfNotSuccessStatusCode(token);

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var eventLine = await reader
            .ReadLineAsync(token)
            .ConfigureAwait(false);

        while (eventLine is not null)
        {
            var line = JsonSerializer
                .Deserialize<Line>(eventLine, _defaultSerializerOptions)
                .ThrowIfNull(eventLine);

            switch (line.Type)
            {
                case "event":
                    line.ThrowIfNotExpectedPayload("event");
                    var cloudEvent = line.Payload.Deserialize<CloudEvent>(_defaultSerializerOptions);
                    if (cloudEvent is null)
                    {
                        throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{line.Payload}' into cloud event.");
                    }

                    yield return new Event(cloudEvent, _dataSerializerOptions);

                    break;
                case "error":
                    line.ThrowIfNotExpectedError();
                    throw new Exception(line.Payload.GetString() ?? "unknown error");
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }

            eventLine = await reader
                .ReadLineAsync(token)
                .ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<string> ReadSubjectsAsync(
        string baseSubject,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var readSubjectsUrl = new Uri(_baseUrl, "/api/v1/read-subjects");

        _logger.LogTrace("Trying to read subjects using url '{Url}'...", readSubjectsUrl);

        var requestBody = new { baseSubject };

        using var request = new HttpRequestMessage(HttpMethod.Post, readSubjectsUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, _defaultSerializerOptions),
            Encoding.UTF8,
            MediaTypeNames.Application.Json
        );

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.ThrowIfNotValidServerHeader();
        await response.ThrowIfNotSuccessStatusCode(token);

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var subjectLine = await reader
            .ReadLineAsync(token)
            .ConfigureAwait(false);

        while (subjectLine is not null)
        {
            var line = JsonSerializer
                .Deserialize<Line>(subjectLine, _defaultSerializerOptions)
                .ThrowIfNull(subjectLine);

            switch (line.Type)
            {
                case "subject":
                    line.ThrowIfNotExpectedPayload("subject");
                    var subject = line.Payload.Deserialize<Subject>(_defaultSerializerOptions);
                    if (subject is null)
                    {
                        throw new InvalidValueException("Failed to deserialize stream subject.");
                    }
                    yield return subject.Name;
                    break;
                case "error":
                    line.ThrowIfNotExpectedError();
                    throw new Exception(line.Payload.GetString() ?? "unknown error");
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }

            subjectLine = await reader
                .ReadLineAsync(token)
                .ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<Event> ObserveEventsAsync(
        string subject,
        ObserveEventsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var observeEventsUrl = new Uri(_baseUrl, "/api/v1/observe-events");

        _logger.LogTrace("Trying to observe events using url '{Url}'...", observeEventsUrl);

        var requestOptions = new ObserveEventsRequestOptions(options);

        using var request = new HttpRequestMessage(HttpMethod.Post, observeEventsUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { subject, Options = requestOptions }, _defaultSerializerOptions),
            Encoding.UTF8,
            MediaTypeNames.Application.Json
        );

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.ThrowIfNotValidServerHeader();
        await response.ThrowIfNotSuccessStatusCode(token);

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var eventLine = await reader
            .ReadLineAsync(token)
            .ConfigureAwait(false);

        while (eventLine is not null)
        {
            var line = JsonSerializer
                .Deserialize<Line>(eventLine, _defaultSerializerOptions)
                .ThrowIfNull(eventLine);

            switch (line.Type)
            {
                case "event":
                    line.ThrowIfNotExpectedPayload("event");
                    var cloudEvent = line.Payload.Deserialize<CloudEvent>(_defaultSerializerOptions);
                    if (cloudEvent is null)
                    {
                        throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{line.Payload}' into cloud event.");
                    }

                    yield return new Event(cloudEvent, _dataSerializerOptions);

                    break;
                case "error":
                    line.ThrowIfNotExpectedError();
                    throw new Exception(line.Payload.GetString() ?? "unknown error");
                case "heartbeat":
                    continue;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }

            eventLine = await reader
                .ReadLineAsync(token)
                .ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<EventType> ReadEventTypesAsync(
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var readEvenTypesUrl = new Uri(_baseUrl, "/api/v1/read-event-types");

        _logger.LogTrace("Trying to read event types using url '{Url}'...", readEvenTypesUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, readEvenTypesUrl);

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.ThrowIfNotValidServerHeader();
        await response.ThrowIfNotSuccessStatusCode(token);

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var eventTypesLine = await reader
            .ReadLineAsync(token)
            .ConfigureAwait(false);

        while (eventTypesLine is not null)
        {
            var line = JsonSerializer
                .Deserialize<Line>(eventTypesLine, _defaultSerializerOptions)
                .ThrowIfNull(eventTypesLine);

            switch (line.Type)
            {
                case "eventType":
                    line.ThrowIfNotExpectedPayload("eventType");
                    var eventType = line.Payload.Deserialize<EventType>(_defaultSerializerOptions);
                    if (eventType is null)
                    {
                        throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{line.Payload}' into event type.");
                    }

                    yield return eventType;

                    break;
                case "error":
                    line.ThrowIfNotExpectedError();
                    throw new Exception(line.Payload.GetString() ?? "unknown error");
                case "heartbeat":
                    continue;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }

            eventTypesLine = await reader
                .ReadLineAsync(token)
                .ConfigureAwait(false);
        }
    }

    public async Task<EventType> ReadEventTypeAsync(
        string eventType,
        CancellationToken token = default)
    {
        var readEventTypeUrl = new Uri(_baseUrl, "/api/v1/read-event-type");

        _logger.LogTrace("Trying to read event type using url '{Url}'...", readEventTypeUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, readEventTypeUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { eventType }, _defaultSerializerOptions),
            Encoding.UTF8,
            MediaTypeNames.Application.Json
        );

        using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        await response.ThrowIfNotSuccessStatusCode(token);

        var eventTypeResponse = await response.Content
            .ReadFromJsonAsync<EventType>(_defaultSerializerOptions, token)
            .ConfigureAwait(false);

        if (eventTypeResponse is null)
        {
            throw new InvalidValueException($"Failed to get the expected response, got null for event type '{eventType}'.");
        }

        return eventTypeResponse;
    }

    public async Task RegisterEventSchemaAsync(
        string eventType,
        JsonElement schema,
        CancellationToken token = default)
    {
        var registerEventSchemaUrl = new Uri(_baseUrl, "/api/v1/register-event-schema");

        var requestBody = new
        {
            eventType,
            schema
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, registerEventSchemaUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, _defaultSerializerOptions),
            Encoding.UTF8,
            MediaTypeNames.Application.Json
        );

        using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        await response.ThrowIfNotSuccessStatusCode(token);
    }

    public async IAsyncEnumerable<TRow?> RunEventQlQueryAsync<TRow>(
        string query,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var runEventQlQueryUrl = new Uri(_baseUrl, "/api/v1/run-eventql-query");

        _logger.LogTrace("Trying to run EventQL query using url '{Url}'...", runEventQlQueryUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, runEventQlQueryUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new RequestBody(query), _defaultSerializerOptions),
            Encoding.UTF8,
            MediaTypeNames.Application.Json
        );

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.ThrowIfNotValidServerHeader();
        await response.ThrowIfNotSuccessStatusCode(token);

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var queryLine = await reader
            .ReadLineAsync(token)
            .ConfigureAwait(false);

        while (queryLine is not null)
        {
            var line = JsonSerializer
                .Deserialize<Line>(queryLine, _defaultSerializerOptions)
                .ThrowIfNull(queryLine);

            switch (line.Type)
            {
                case "row":
                    yield return DeserializeRow<TRow>(line.Payload);
                    break;
                case "error":
                    line.ThrowIfNotExpectedError();
                    throw new Exception(line.Payload.GetString() ?? "unknown error");
                case "heartbeat":
                    continue;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }

            queryLine = await reader
                .ReadLineAsync(token)
                .ConfigureAwait(false);
        }
    }

    private TRow? DeserializeRow<TRow>(JsonElement payload)
    {
        try
        {
            if (typeof(TRow) != typeof(Event))
            {
                return payload.Deserialize<TRow>(_defaultSerializerOptions);
            }

            var cloudEvent = payload.Deserialize<CloudEvent>(_defaultSerializerOptions);
            if (cloudEvent is null)
            {
                throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{payload}' into cloud event.");
            }

            // At design time, there is no type conversion between TRow and Event, so we need to cast to object first
            return (TRow)(object)new Event(cloudEvent, _dataSerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidValueException($"Failed to deserialize query result into type '{typeof(TRow).Name}': {ex.Message}");
        }
    }
}
