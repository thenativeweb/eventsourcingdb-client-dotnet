using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public class Client
{
    private static readonly JsonSerializerOptions _defaultSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

    };
    private readonly JsonSerializerOptions? _dataSerializerOptions;
    private static readonly HttpClient _httpClient = new HttpClient(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }
    );
    private readonly Uri _baseUrl;
    private readonly string _apiToken;
    private readonly ILogger<Client> _logger;

    public Client(Uri baseUrl, string apiToken)
    {
        _baseUrl = baseUrl;
        _apiToken = apiToken;
        _logger = NullLogger<Client>.Instance;
    }

    public Client(Uri baseUrl, string apiToken, ILogger<Client>? logger = null)
    {
        _baseUrl = baseUrl;
        _apiToken = apiToken;
        _logger = logger ?? NullLogger<Client>.Instance;
    }

    public Client(Uri baseUrl, string apiToken, JsonSerializerOptions? dataSerializerOptions = null, ILogger<Client>? logger = null)
        : this(baseUrl, apiToken, logger)
    {
        _baseUrl = baseUrl;
        _apiToken = apiToken;
        _logger = logger ?? NullLogger<Client>.Instance;
        _dataSerializerOptions = dataSerializerOptions;
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
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException(
                    message: "Unexpected status code.", inner: null, statusCode: response.StatusCode
                );
            }

            var pingResponse = await response.Content
                .ReadFromJsonAsync<VerifyApiTokenResponse>(_defaultSerializerOptions, token)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(pingResponse.Type))
            {
                throw new InvalidValueException($"Failed to get the expected response, got empty string, expected '{expectedEventType}'.");
            }

            if (pingResponse.Type != expectedEventType)
            {
                throw new InvalidValueException($"Failed to get the expected response, got '{pingResponse.Type}' expected '{expectedEventType}'.");
            }

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
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

            using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException(
                    message: "Unexpected status code.", inner: null, statusCode: response.StatusCode
                );
            }

            var verifyResponse = await response.Content
                .ReadFromJsonAsync<VerifyApiTokenResponse>(_defaultSerializerOptions, token)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(verifyResponse.Type))
            {
                throw new InvalidValueException($"Failed to get the expected response, got empty string, expected '{expectedEventType}'.");
            }

            if (verifyResponse.Type != expectedEventType)
            {
                throw new InvalidValueException($"Failed to get the expected response, got '{verifyResponse.Type}' expected '{expectedEventType}'.");
            }

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
            var candidatesWithSerializedData =
                events.Select(e => e with { Data = JsonSerializer.SerializeToElement(e.Data, _dataSerializerOptions) });

            using var request = new HttpRequestMessage(HttpMethod.Post, writeEventsUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { events = candidatesWithSerializedData, preconditions }, _defaultSerializerOptions),
                Encoding.UTF8,
                "application/json"
            );

            using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var errorResponse = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                throw new HttpRequestException(
                    message: $"Unexpected status code ('{errorResponse}').", inner: null, statusCode: response.StatusCode
                );
            }

            var eventsResponse = await response.Content
                .ReadFromJsonAsync<CloudEvent[]>(_defaultSerializerOptions, token)
                .ConfigureAwait(false);

            if (eventsResponse == null) throw new InvalidValueException("Failed to parse response.");

            var result = eventsResponse.Select(cloudEvent => new Event(cloudEvent)).ToArray();

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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { subject, Options = requestOptions }, _defaultSerializerOptions),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                message: "Unexpected status code.", inner: null, statusCode: response.StatusCode
            );
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var eventResponse = await reader
            .ReadLineAsync(token)
            .ConfigureAwait(false);

        while (eventResponse != null)
        {
            var line = JsonSerializer.Deserialize<ReadEventLine>(eventResponse, _defaultSerializerOptions);
            if (line?.Type == null)
            {
                throw new InvalidValueException($"Failed to get the expected response, got null line from '{eventResponse}'.");
            }

            switch (line.Type)
            {
                case "event":
                    if (line.Payload.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidValueException($"Received line of type 'event', but payload is not an object: '{line.Payload}'.");
                    }
                    var cloudEvent = line.Payload.Deserialize<CloudEvent>(_defaultSerializerOptions);
                    if (cloudEvent == null)
                    {
                        throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{line.Payload}' into cloud event.");
                    }

                    yield return new Event(cloudEvent);

                    break;
                case "error":
                    if (line.Payload.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidValueException($"Received line of type 'error', but payload is not a string: '{line.Payload}'.");
                    }
                    throw new Exception(line.Payload.GetString() ?? "unknown error");
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }

            eventResponse = await reader
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { subject, Options = requestOptions }, _defaultSerializerOptions),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                message: "Unexpected status code.", inner: null, statusCode: response.StatusCode
            );
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var eventResponse = await reader
            .ReadLineAsync(token)
            .ConfigureAwait(false);

        while (eventResponse != null)
        {
            var line = JsonSerializer.Deserialize<ReadEventLine>(eventResponse, _defaultSerializerOptions);
            if (line?.Type == null)
            {
                throw new InvalidValueException($"Failed to get the expected response, got null line from '{eventResponse}'.");
            }

            switch (line.Type)
            {
                case "event":
                    if (line.Payload.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidValueException($"Received line of type 'event', but payload is not an object: '{line.Payload}'.");
                    }
                    var cloudEvent = line.Payload.Deserialize<CloudEvent>(_defaultSerializerOptions);
                    if (cloudEvent == null)
                    {
                        throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{line.Payload}' into cloud event.");
                    }

                    yield return new Event(cloudEvent);

                    break;
                case "error":
                    if (line.Payload.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidValueException($"Received line of type 'error', but payload is not a string: '{line.Payload}'.");
                    }
                    throw new Exception(line.Payload.GetString() ?? "unknown error");
                case "heartbeat":
                    continue;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }

            eventResponse = await reader
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                message: "Unexpected status code.", inner: null, statusCode: response.StatusCode
            );
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var eventTypesResponse = await reader
            .ReadLineAsync(token)
            .ConfigureAwait(false);

        while (eventTypesResponse != null)
        {
            var line = JsonSerializer.Deserialize<ReadEventLine>(eventTypesResponse, _defaultSerializerOptions);
            if (line?.Type == null)
            {
                throw new InvalidValueException($"Failed to get the expected response, got null line from '{eventTypesResponse}'.");
            }

            switch (line.Type)
            {
                case "eventType":
                    if (line.Payload.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidValueException($"Received line of type 'eventType', but payload is not an object: '{line.Payload}'.");
                    }
                    var eventType = line.Payload.Deserialize<EventType>(_defaultSerializerOptions);
                    if (eventType == null)
                    {
                        throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{line.Payload}' into event type.");
                    }

                    yield return eventType;

                    break;
                case "error":
                    if (line.Payload.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidValueException($"Received line of type 'error', but payload is not a string: '{line.Payload}'.");
                    }
                    throw new Exception(line.Payload.GetString() ?? "unknown error");
                case "heartbeat":
                    continue;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }

            eventTypesResponse = await reader
                .ReadLineAsync(token)
                .ConfigureAwait(false);
        }
    }

    public async Task<EventType> ReadEventTypeAsync(
        string eventType,
        CancellationToken token = default)
    {
        var readEventTypeUrl = new Uri(_baseUrl, $"/api/v1/read-event-type");

        _logger.LogTrace("Trying to read event type using url '{Url}'...", readEventTypeUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, readEventTypeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { eventType }, _defaultSerializerOptions),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                message: "Unexpected status code.", inner: null, statusCode: response.StatusCode
            );
        }

        var eventTypeResponse = await response.Content
            .ReadFromJsonAsync<EventType>(_defaultSerializerOptions, token)
            .ConfigureAwait(false);

        if (eventTypeResponse == null)
        {
            throw new InvalidValueException($"Failed to get the expected response, got null for event type '{eventType}'.");
        }

        return eventTypeResponse;
    }

    public async IAsyncEnumerable<JsonElement> RunEventQlQueryAsync(
        string query,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var runEventQlQueryUrl = new Uri(_baseUrl, "/api/v1/run-eventql-query");

        _logger.LogTrace("Trying to run EventQL query using url '{Url}'...", runEventQlQueryUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, runEventQlQueryUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new RequestBody(query), _defaultSerializerOptions),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                message: "Unexpected status code.", inner: null, statusCode: response.StatusCode
            );
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var queryResponse = await reader
            .ReadLineAsync(token)
            .ConfigureAwait(false);

        while (queryResponse != null)
        {
            var line = JsonSerializer.Deserialize<ReadEventLine>(queryResponse, _defaultSerializerOptions);
            if (line?.Type == null)
            {
                throw new InvalidValueException($"Failed to get the expected response, got null line from '{queryResponse}'.");
            }

            switch (line.Type)
            {
                case "row":
                    yield return line.Payload;
                    break;
                case "error":
                    if (line.Payload.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidValueException($"Received line of type 'error', but payload is not a string: '{line.Payload}'.");
                    }
                    throw new Exception(line.Payload.GetString() ?? "unknown error");
                case "heartbeat":
                    continue;
                default:
                    throw new Exception($"Failed to handle unsupported line type '{line.Type}'.");
            }

            queryResponse = await reader
                .ReadLineAsync(token)
                .ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<TRow> RunEventQlQueryAsync<TRow>(
        string query,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        await foreach (var jsonElement in RunEventQlQueryAsync(query, token))
        {
            TRow? row;
            try
            {
                row = jsonElement.Deserialize<TRow>(_defaultSerializerOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidValueException($"Failed to deserialize query result into type '{typeof(TRow).Name}': {ex.Message}");
            }

            yield return row!;
        }
    }
}
