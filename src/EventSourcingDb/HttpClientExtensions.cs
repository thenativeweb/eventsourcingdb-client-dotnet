using System.Net.Http;
using System.Net.Http.Headers;

namespace EventSourcingDb;

public static class HttpClientExtensions
{
    public static HttpClient AuthorizeWithBearerToken(this HttpClient httpClient, string apiToken)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        return httpClient;
    }
}
