using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EventSourcingDb;

public class Client
{
    private readonly Uri _baseUrl;
    private readonly string _apiToken;
    private static readonly HttpClient _httpClient = new();

    public Client(Uri baseUrl, string apiToken)
    {
        this._baseUrl = baseUrl;
        this._apiToken = apiToken;
    }

    public async Task PingAsync()
    {
        var pingUrl = new Uri(_baseUrl, "/api/v1/ping");

        using var request = new HttpRequestMessage(HttpMethod.Get, pingUrl);
        using var response = await Client._httpClient.SendAsync(request);

        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new Exception($"Failed to ping, got HTTP status code '{(int)response.StatusCode}', expected '200'.");
        }

        using var responseStream = await response.Content.ReadAsStreamAsync();
        using var responseJson = await JsonDocument.ParseAsync(responseStream);

        if (!responseJson.RootElement.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new Exception("Failed to parse response.");
        }

        if (typeElement.GetString() != "io.eventsourcingdb.api.ping-received")
        {
            throw new Exception("Failed to ping.");
        }
    }
}
