using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

public class Client
{
    private readonly Uri _baseUrl;
    private readonly string _apiToken;
    private static readonly HttpClient _httpClient = new HttpClient(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }
    );

    public Client(Uri baseUrl, string apiToken)
    {
        _baseUrl = baseUrl;
        _apiToken = apiToken;
    }

    public async Task PingAsync(CancellationToken token = default)
    {
        var pingUrl = new Uri(_baseUrl, "/api/v1/ping");

        using var request = new HttpRequestMessage(HttpMethod.Get, pingUrl);
        using var response = await Client._httpClient.SendAsync(request, token).ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new HttpRequestException($"Failed to ping, got HTTP status code '{(int)response.StatusCode}', expected '200'.");
        }

        using var responseStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var responseJson = await JsonDocument.ParseAsync(responseStream, cancellationToken: token).ConfigureAwait(false);

        if (!responseJson.RootElement.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Failed to parse response. Property Type ist not a string");
        }

        var typeString = typeElement.GetString();

        if (string.IsNullOrEmpty(typeString))
        {
            throw new InvalidValueException("Failed to ping, empty string got expected io.eventsourcingdb.api.ping-received");
        }

        if (typeString != "io.eventsourcingdb.api.ping-received")
        {
            throw new InvalidValueException($"Failed to ping, got '{typeString}' expected io.eventsourcingdb.api.ping-received");
        }
    }
}
