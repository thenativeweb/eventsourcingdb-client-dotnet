using System;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EventSourcingDb;

// What both APIs share: the HttpClient, the stall timeout that bounds every request, the
// serialization, and the logger.
internal sealed class ServerConnection(
    HttpClient httpClient,
    TimeSpan stallTimeout,
    JsonSerializerOptions dataSerializerOptions,
    ILogger<Client> logger)
{
    public static readonly JsonSerializerOptions DefaultSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public HttpClient HttpClient { get; } = httpClient;

    // A request fails once nothing arrives from the server for this long, from sending it until
    // its end. Everything that arrives starts the time anew, so it does not limit how long a
    // stream lasts. EventSourcingDB sends heartbeats on its streams, so these never fall silent
    // while the connection is intact.
    public TimeSpan StallTimeout { get; } = stallTimeout;

    public JsonSerializerOptions DataSerializerOptions { get; } = dataSerializerOptions;

    public ILogger<Client> Logger { get; } = logger;

    public static StringContent CreateJsonContent(object body)
    {
        return new StringContent(
            JsonSerializer.Serialize(body, DefaultSerializerOptions),
            Encoding.UTF8,
            MediaTypeNames.Application.Json
        );
    }

    // Sends a request and fails with a ConnectionStalledException once nothing arrives from the
    // server for the stall timeout, whether while waiting for the response headers, while reading
    // a response that is read completely before it is returned (ResponseContentRead), or while
    // reading the content of a streamed response (ResponseHeadersRead) later on.
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken token)
    {
        HttpResponseMessage response;

        using (var stallSource = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            stallSource.CancelAfter(StallTimeout);

            try
            {
                response = await HttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stallSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stallSource.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw CreateConnectionStalledException();
            }
            catch (OperationCanceledException exception) when (token.IsCancellationRequested && exception.CancellationToken != token)
            {
                // HttpClient only knows the linked token, but callers tell their own cancellation
                // apart by its token, so the exception must carry the caller's token.
                throw new TaskCanceledException(exception.Message, exception, token);
            }
        }

        response.Content = new StallDetectingContent(response.Content, StallTimeout, CreateConnectionStalledException);

        if (completionOption is HttpCompletionOption.ResponseContentRead)
        {
            try
            {
                // Reading the content buffers it, so it can be read again afterwards.
                await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        return response;
    }

    // Reads an answer that is not 200 as the error that the server reports with it, which is
    // plain text.
    public static async Task<DbApiException> ReadStatusErrorAsync(
        HttpResponseMessage response,
        Func<HttpStatusCode, string, string?> getCode,
        CancellationToken token)
    {
        var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

        // The server ends the text with a line break, which is not part of the description.
        var description = text.TrimEnd('\r', '\n');

        return new DbApiException(
            response.StatusCode,
            description,
            getCode(response.StatusCode, description),
            retryAfter: GetRetryAfter(response)
        );
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.StatusCode is not HttpStatusCode.TooManyRequests)
        {
            return null;
        }

        return response.Headers.RetryAfter?.Delta;
    }

    private ConnectionStalledException CreateConnectionStalledException()
    {
        return new ConnectionStalledException(
            FormattableString.Invariant($"Nothing arrived from the server for {StallTimeout.TotalSeconds} seconds.")
        );
    }
}
