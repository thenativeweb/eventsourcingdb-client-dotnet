using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb.Tests;

// A request as it arrived at a FakeServer, with its Authorization header, if it has one.
internal sealed record FakeRequest(string Method, string Path, string? Body, string? Authorization = null);

// Stands in for EventSourcingDB within the process, as the handler of an HttpClient. It records
// every request and answers it with what respond returns.
internal sealed class FakeServer(Func<FakeRequest, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public const string ProbePath = "/api/v2/system/ping";

    private readonly ConcurrentQueue<FakeRequest> _requests = new();

    public FakeServer(Func<FakeRequest, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public IReadOnlyList<FakeRequest> Requests => _requests.ToArray();

    public int ProbeCount => _requests.Count(IsProbe);

    // Answers the probe as a server that offers the given API does, and every other request with
    // what respond returns.
    public static FakeServer ForApi(string apiVersion, Func<FakeRequest, HttpResponseMessage> respond)
    {
        var isProbeAnswered = 0;

        return new FakeServer(request =>
        {
            if (IsProbe(request) && Interlocked.Exchange(ref isProbeAnswered, 1) == 0)
            {
                return AnswerProbe(apiVersion);
            }

            return respond(request);
        });
    }

    public static bool IsProbe(FakeRequest request) => request is { Method: "GET", Path: ProbePath };

    public static HttpResponseMessage AnswerProbe(string apiVersion)
    {
        return apiVersion switch
        {
            "v1" => Text(HttpStatusCode.NotFound, "404 page not found\n"),
            "v2" => Json(HttpStatusCode.OK, """{"type":"event","data":{"specversion":"1.0","id":"0","source":"https://www.eventsourcingdb.io","type":"io.eventsourcingdb.api.ping-received","subject":"/api/v2/system/ping","time":"2026-10-18T12:00:00Z","datacontenttype":"application/json","data":{"message":"My God, it's full of stars."}}}"""),
            _ => throw new ArgumentOutOfRangeException(nameof(apiVersion), apiVersion, "Unknown API version."),
        };
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) => Respond(status, body, "application/json");

    public static HttpResponseMessage Ndjson(params string[] lines) => Respond(HttpStatusCode.OK, string.Concat(lines.Select(line => line + "\n")), "application/x-ndjson");

    public static HttpResponseMessage Text(HttpStatusCode status, string body) => Respond(status, body, "text/plain");

    public static HttpResponseMessage Respond(HttpStatusCode status, string body, string contentType, bool isFromEventSourcingDb = true)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };

        if (isFromEventSourcingDb)
        {
            response.Headers.TryAddWithoutValidation("Server", "EventSourcingDB/0.0.0");
        }

        return response;
    }

    public Client CreateClient()
    {
        return new Client(new HttpClient(this) { BaseAddress = new Uri("http://esdb.test") });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The body is read as a connection reads it, without loading it into a buffer, which
        // would keep the client from telling how often a write was sent.
        string? body = null;
        if (request.Content is not null)
        {
            using var buffer = new MemoryStream();
            await request.Content.CopyToAsync(buffer, cancellationToken);
            body = Encoding.UTF8.GetString(buffer.ToArray());
        }
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(", ", values) : null;
        var fakeRequest = new FakeRequest(request.Method.Method, request.RequestUri!.AbsolutePath, body, authorization);

        _requests.Enqueue(fakeRequest);

        return await respond(fakeRequest, cancellationToken);
    }
}

// Answers the probe of the client as a server that offers the given API does, and passes every
// other request on to the network, so that a test server over a real connection only sees the
// requests that follow the probe.
internal sealed class ProbeAnsweringHandler(string apiVersion, HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    public ProbeAnsweringHandler(string apiVersion) : this(apiVersion, new SocketsHttpHandler())
    {
    }

    private int _isProbeAnswered;

    public static Client CreateClient(string apiVersion, Uri baseUrl, TimeSpan stallTimeout)
    {
        var httpClient = new HttpClient(new ProbeAnsweringHandler(apiVersion))
        {
            BaseAddress = baseUrl,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        return new Client(httpClient, new ClientOptions { StallTimeout = stallTimeout }, null, null);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get &&
            request.RequestUri!.AbsolutePath == FakeServer.ProbePath &&
            Interlocked.Exchange(ref _isProbeAnswered, 1) == 0)
        {
            return Task.FromResult(FakeServer.AnswerProbe(apiVersion));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
