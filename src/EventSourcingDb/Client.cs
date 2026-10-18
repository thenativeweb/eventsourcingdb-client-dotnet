using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventSourcingDb;

// The client talks to each server through the API that the server offers: v2 since
// EventSourcingDB 1.3, and v1 before. Creating it makes no request; the first request of an
// instance, or of any client of the same registration for dependency injection, finds out which
// API the server offers, and every request uses that API from then on. This is the one place
// that chooses between the two.
public class Client : IClient
{
    private const string ProbeUrl = "/api/v2/system/ping";

    private static readonly TimeSpan _maxStallTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly ServerConnection _connection;
    private readonly ApiVersionDetector _apiVersionDetector;
    private readonly V1Transport _v1Transport;
    private readonly V2Transport _v2Transport;

    public Client(Uri baseUrl, string apiToken) : this(CreateHttpClient(baseUrl, apiToken), new ClientOptions(), null, null)
    {
    }

    public Client(Uri baseUrl, string apiToken, ILogger<Client>? logger = null) : this(CreateHttpClient(baseUrl, apiToken), new ClientOptions(), null, logger)
    {
    }

    public Client(Uri baseUrl, string apiToken, JsonSerializerOptions? dataSerializerOptions = null, ILogger<Client>? logger = null)
        : this(CreateHttpClient(baseUrl, apiToken), new ClientOptions(), dataSerializerOptions, logger)
    {
    }

    public Client(Uri baseUrl, string apiToken, ClientOptions options, JsonSerializerOptions? dataSerializerOptions = null, ILogger<Client>? logger = null)
        : this(CreateHttpClient(baseUrl, apiToken), options, dataSerializerOptions, logger)
    {
    }

    // Typed HttpClient registrations create the client through ActivatorUtilities, which needs to
    // know which of the constructors that take an HttpClient to use.
    [ActivatorUtilitiesConstructor]
    public Client(HttpClient httpClient, JsonSerializerOptions? dataSerializerOptions = null, ILogger<Client>? logger = null)
        : this(httpClient, new ClientOptions(), dataSerializerOptions, logger)
    {
    }

    // All parameters are required, since optional ones would make a call such as
    // new Client(httpClient, null) ambiguous with the constructor above.
    public Client(HttpClient httpClient, ClientOptions options, JsonSerializerOptions? dataSerializerOptions, ILogger<Client>? logger)
        : this(httpClient, options, dataSerializerOptions, logger, new ApiVersionDetector())
    {
    }

    // The clients of a registration for dependency injection share the detector, so that each
    // of them does not probe on its own.
    internal Client(
        HttpClient httpClient,
        ClientOptions options,
        JsonSerializerOptions? dataSerializerOptions,
        ILogger<Client>? logger,
        ApiVersionDetector apiVersionDetector)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.StallTimeout <= TimeSpan.Zero || options.StallTimeout > _maxStallTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.StallTimeout,
                FormattableString.Invariant($"The stall timeout must be greater than zero and at most {_maxStallTimeout.TotalMilliseconds} milliseconds.")
            );
        }

        _connection = new ServerConnection(
            httpClient,
            options.StallTimeout,
            dataSerializerOptions ?? CreateDefaultDataSerializerOptions(),
            logger ?? NullLogger<Client>.Instance
        );
        _apiVersionDetector = apiVersionDetector;
        _v1Transport = new V1Transport(_connection);
        _v2Transport = new V2Transport(_connection);
    }

    // A request fails once nothing arrives from the server for this long, from sending it until
    // its end. See ServerConnection.StallTimeout.
    internal TimeSpan StallTimeout => _connection.StallTimeout;

    internal HttpClient HttpClient => _connection.HttpClient;

    public async Task PingAsync(CancellationToken token = default)
    {
        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        await transport.PingAsync(token).ConfigureAwait(false);
    }

    public async Task VerifyApiTokenAsync(CancellationToken token = default)
    {
        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        await transport.VerifyApiTokenAsync(token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Event>> WriteEventsAsync(
        IEnumerable<EventCandidate> events,
        IEnumerable<Precondition>? preconditions = null,
        CancellationToken token = default)
    {
        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        return await transport.WriteEventsAsync(events, preconditions, token).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<Event> ReadEventsAsync(
        string subject,
        ReadEventsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        await foreach (var @event in transport.ReadEventsAsync(subject, options, token).ConfigureAwait(false))
        {
            yield return @event;
        }
    }

    public async IAsyncEnumerable<string> ReadSubjectsAsync(
        string subject,
        ReadSubjectsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        await foreach (var readSubject in transport.ReadSubjectsAsync(subject, options, token).ConfigureAwait(false))
        {
            yield return readSubject;
        }
    }

    public async IAsyncEnumerable<Event> ObserveEventsAsync(
        string subject,
        ObserveEventsOptions options,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        await foreach (var @event in transport.ObserveEventsAsync(subject, options, token).ConfigureAwait(false))
        {
            yield return @event;
        }
    }

    public async IAsyncEnumerable<EventType> ReadEventTypesAsync(
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        await foreach (var eventType in transport.ReadEventTypesAsync(token).ConfigureAwait(false))
        {
            yield return eventType;
        }
    }

    public async Task<EventType> ReadEventTypeAsync(
        string eventType,
        CancellationToken token = default)
    {
        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        return await transport.ReadEventTypeAsync(eventType, token).ConfigureAwait(false);
    }

    public async Task RegisterEventSchemaAsync(
        string eventType,
        JsonElement schema,
        CancellationToken token = default)
    {
        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        await transport.RegisterEventSchemaAsync(eventType, schema, token).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TRow?> RunEventQlQueryAsync<TRow>(
        string query,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var transport = await GetTransportAsync(token).ConfigureAwait(false);
        await foreach (var row in transport.RunEventQlQueryAsync<TRow>(query, token).ConfigureAwait(false))
        {
            yield return row;
        }
    }

    private async Task<IClient> GetTransportAsync(CancellationToken token)
    {
        var apiVersion = await _apiVersionDetector.DetectAsync(ProbeAsync, token).ConfigureAwait(false);

        return apiVersion switch
        {
            ApiVersion.V1 => _v1Transport,
            ApiVersion.V2 => _v2Transport,
            _ => throw new InvalidOperationException($"Unhandled API version '{apiVersion}'."),
        };
    }

    // Asks the server for the ping of the v2 API. A server that offers it answers with an event
    // in an envelope of the v2 API. A server before EventSourcingDB 1.3 does not know the path,
    // and answers with 404, or with 405 where its UI serves other methods. Such an answer means
    // v1 even if it does not come from EventSourcingDB, as from a proxy that passes on only the
    // paths of the v1 API, since every request to the v1 API checks that its answer does. Any
    // other answer is no definitive one, so the probe fails with it.
    private async Task<ApiVersion> ProbeAsync()
    {
        var logger = _connection.Logger;
        logger.LogTrace("Trying to find out which API the server offers using url '{Url}'...", ProbeUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
            using var response = await _connection
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, CancellationToken.None)
                .ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed))
            {
                response.ThrowIfNotValidServerHeader();
            }

            var apiVersion = response.StatusCode switch
            {
                HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed => ApiVersion.V1,
                HttpStatusCode.OK => await ReadV2PingAsync(response).ConfigureAwait(false),
                _ => throw await ServerConnection
                    .ReadStatusErrorAsync(response, (_, _) => null, CancellationToken.None)
                    .ConfigureAwait(false),
            };

            logger.LogTrace("The server offers the API {ApiVersion}.", apiVersion);

            return apiVersion;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to find out which API the server offers using url '{Url}'.", ProbeUrl);
            throw;
        }
    }

    private static async Task<ApiVersion> ReadV2PingAsync(HttpResponseMessage response)
    {
        var rawEnvelope = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(rawEnvelope, ServerConnection.DefaultSerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidValueException($"Failed to find out which API the server offers, the ping of the v2 API answered '{rawEnvelope}': {exception.Message}");
        }

        if (envelope is not { Type: "event", Data.ValueKind: JsonValueKind.Object })
        {
            throw new InvalidValueException($"Failed to find out which API the server offers, the ping of the v2 API answered '{rawEnvelope}'.");
        }

        return ApiVersion.V2;
    }

    private static JsonSerializerOptions CreateDefaultDataSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    private static HttpClient CreateHttpClient(Uri baseUrl, string apiToken)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        // The client bounds every request by its stall timeout, so the timeout of HttpClient, which
        // would also bound streams, must not cut requests short.
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = baseUrl,
            Timeout = Timeout.InfiniteTimeSpan
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        return httpClient;
    }
}
