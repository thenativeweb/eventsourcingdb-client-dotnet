using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;

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

    public Client(Uri baseUrl, string apiToken)
    {
        _baseUrl = baseUrl;
        _apiToken = apiToken;
    }

    public async Task PingAsync(CancellationToken token = default)
    {
        var pingUrl = new Uri(_baseUrl, "/api/v1/ping");

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
    }
}
