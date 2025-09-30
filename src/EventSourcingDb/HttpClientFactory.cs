using System;
using System.Net.Http;

namespace EventSourcingDb;

public class HttpClientFactory
{
    public static HttpClient GetConfiguredDefaultClient(Uri baseUrl, string apiToken)
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };

        return new HttpClient(handler) { BaseAddress = baseUrl }.AuthorizeWithBearerToken(apiToken);
    }
}
