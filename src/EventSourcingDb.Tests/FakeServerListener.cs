using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb.Tests;

// Serves a FakeServer over real connections, so that it also stands in for EventSourcingDB for a
// client that creates its HttpClient on its own. It reads every request of a connection, hands
// it to the FakeServer, and writes back its answer with a body of a known length, so that the
// client can send its next request over the same connection.
internal sealed class FakeServerListener : IAsyncDisposable
{
    private readonly HttpMessageInvoker _server;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly Task _acceptTask;

    public FakeServerListener(FakeServer server)
    {
        _server = new HttpMessageInvoker(server, disposeHandler: false);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = new Uri($"http://127.0.0.1:{port}");

        _acceptTask = AcceptAsync(_stopSource.Token);
    }

    public Uri BaseUrl { get; }

    private async Task AcceptAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(token);
                _connections.Add(ServeAsync(tcpClient, token));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Ignored, the server is being disposed
        }
    }

    private async Task ServeAsync(TcpClient tcpClient, CancellationToken token)
    {
        using (tcpClient)
        {
            try
            {
                await using var stream = tcpClient.GetStream();

                while (await ReadRequestAsync(stream, token) is { } request)
                {
                    using (request)
                    {
                        using var response = await _server.SendAsync(request, token);
                        await WriteResponseAsync(stream, response, token);
                    }
                }
            }
            catch (IOException)
            {
                // Ignored, the client has closed the connection
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Ignored, the server is being disposed
            }
        }
    }

    // Reads the next request of a connection, or returns null once the client has closed it.
    private async Task<HttpRequestMessage?> ReadRequestAsync(NetworkStream stream, CancellationToken token)
    {
        var head = new StringBuilder();
        var buffer = new byte[1];

        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(buffer, token) == 0)
            {
                return head.Length == 0
                    ? null
                    : throw new IOException("Connection closed while reading the request.");
            }

            head.Append((char)buffer[0]);
        }

        var lines = head.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var requestLine = lines[0].Split(' ');
        var request = new HttpRequestMessage(new HttpMethod(requestLine[0]), new Uri(BaseUrl, requestLine[1]));

        var contentLength = 0;
        foreach (var headerLine in lines.Skip(1))
        {
            var separatorIndex = headerLine.IndexOf(':', StringComparison.Ordinal);
            var name = headerLine[..separatorIndex];
            var value = headerLine[(separatorIndex + 1)..].Trim();

            if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("A request with a body of an unknown length is not supported.");
            }

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(value);
                continue;
            }

            // Content headers do not belong to the request, and the FakeServer does not look at
            // them.
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (contentLength > 0)
        {
            var body = new byte[contentLength];
            await stream.ReadExactlyAsync(body, token);
            request.Content = new ByteArrayContent(body);
        }

        return request;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, HttpResponseMessage response, CancellationToken token)
    {
        var body = await response.Content.ReadAsByteArrayAsync(token);

        var head = new StringBuilder();
        head.Append($"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n");
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                head.Append($"{name}: {string.Join(", ", values)}\r\n");
            }
        }
        head.Append($"Content-Length: {body.Length}\r\n\r\n");

        await stream.WriteAsync(Encoding.UTF8.GetBytes(head.ToString()), token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopSource.CancelAsync();
        _listener.Stop();

        await _acceptTask;
        await Task.WhenAll(_connections);

        _server.Dispose();
        _stopSource.Dispose();
    }
}
