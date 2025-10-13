using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb;

public static class HttpResponseExtensions
{
    public static void ThrowIfNotValidServerHeader(this HttpResponseMessage response)
    {
        var serverHeader = response.Headers.Server.ToString();

        if (string.IsNullOrEmpty(serverHeader) || !serverHeader.StartsWith("EventSourcingDB/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Server must be EventSourcingDB.");
        }
    }

    public static async Task ThrowIfNotSuccessStatusCode(
        this HttpResponseMessage response,
        CancellationToken cancellationToken = default
    )
    {
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            var errorResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                message: $"Unexpected status code ('{errorResponse}').", inner: null, statusCode: response.StatusCode
            );
        }
    }
}
