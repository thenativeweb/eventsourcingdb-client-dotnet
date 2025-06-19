using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly HttpClient _httpClient = new HttpClient(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }
    );
    private readonly Uri _baseUrl;
    private readonly string _apiToken;
    private readonly ILogger<Client> _logger;

    public Client(Uri baseUrl, string apiToken, ILogger<Client>? logger = null)
    {
        _baseUrl = baseUrl;
        _apiToken = apiToken;
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

    public async Task<IReadOnlyList<Event>> WriteEvents(
        IEnumerable<EventCandidate> events,
        IEnumerable<Precondition>? preconditions = null,
        CancellationToken token = default)
    {
        preconditions ??= [];
        var writeEventsUrl = new Uri(_baseUrl, "/api/v1/write-events");

        _logger.LogTrace("Trying to write events using url '{Url}'...", writeEventsUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, writeEventsUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { events, preconditions }, _defaultSerializerOptions),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException(
                    message: "Unexpected status code.", inner: null, statusCode: response.StatusCode
                );
            }

            var eventsResponse = await response.Content
                .ReadFromJsonAsync<CloudEvent[]>(_defaultSerializerOptions, token)
                .ConfigureAwait(false);

            if (eventsResponse == null) throw new InvalidValueException("Failed to parse response.");

            var result = eventsResponse.Select(cloudEvent => cloudEvent.ToEvent()).ToArray();

            _logger.LogTrace("Written '{Count}' events using url '{Url}' successfully.", result.Length, writeEventsUrl);

            return result;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to write events using url '{Url}'.", writeEventsUrl);
            throw;
        }
    }
}
