using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        PropertyNameCaseInsensitive = true
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
}
