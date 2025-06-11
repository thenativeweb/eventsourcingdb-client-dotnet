using System;
using System.Net.Http;
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
        var pingUrl = new Uri(_baseUrl, "/api/v1/ping");
        _logger.LogTrace("Trying to ping {Url}", pingUrl);

        try
        {
            var response = await _httpClient
                .GetFromJsonAsync<PingResponse>(pingUrl, _defaultSerializerOptions, token)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(response.Type))
            {
                throw new InvalidValueException("Failed to ping, empty string got expected 'io.eventsourcingdb.api.ping-received'.");
            }

            if (response.Type != "io.eventsourcingdb.api.ping-received")
            {
                throw new InvalidValueException($"Failed to ping, got '{response.Type}' expected 'io.eventsourcingdb.api.ping-received'.");
            }

            _logger.LogTrace("Pinging {Url} succeeded", pingUrl);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to ping {Url}", pingUrl);
            throw;
        }
    }
}
