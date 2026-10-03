using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb.Tests;

// Stands in for EventSourcingDB on a streaming endpoint. It answers the first request with the lines
// that sendLines sends, and then keeps the connection open without sending anything else, which is
// what a stalled connection looks like to the client.
internal sealed class NdjsonTestServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly TaskCompletionSource _connectionClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _serveTask;

    public NdjsonTestServer(Func<Func<string, Task>, CancellationToken, Task> sendLines)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = new Uri($"http://127.0.0.1:{port}");

        _serveTask = ServeAsync(sendLines, _stopSource.Token);
    }

    public Uri BaseUrl { get; }

    // Completes once the client has closed the connection.
    public Task ConnectionClosed => _connectionClosed.Task;

    private async Task ServeAsync(Func<Func<string, Task>, CancellationToken, Task> sendLines, CancellationToken token)
    {
        try
        {
            using var tcpClient = await _listener.AcceptTcpClientAsync(token);
            await using var stream = tcpClient.GetStream();

            await ReadRequestAsync(stream, token);
            await WriteAsync(
                stream,
                "HTTP/1.1 200 OK\r\n" +
                "Server: EventSourcingDB/0.0.0\r\n" +
                "Content-Type: application/x-ndjson\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n",
                token
            );

            await sendLines(line =>
            {
                var chunk = line + "\n";
                return WriteAsync(stream, $"{Encoding.UTF8.GetByteCount(chunk):x}\r\n{chunk}\r\n", token);
            }, token);

            // The client sends nothing more, so reading only returns once it closes the connection.
            var buffer = new byte[1];
            while (await stream.ReadAsync(buffer, token) > 0)
            {
            }

            _connectionClosed.TrySetResult();
        }
        catch (IOException)
        {
            _connectionClosed.TrySetResult();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Ignored, the server is being disposed
        }
    }

    private static async Task ReadRequestAsync(NetworkStream stream, CancellationToken token)
    {
        var head = new StringBuilder();
        var buffer = new byte[1];

        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(buffer, token) == 0)
            {
                throw new IOException("Connection closed while reading the request.");
            }

            head.Append((char)buffer[0]);
        }

        var contentLength = 0;
        foreach (var headerLine in head.ToString().Split("\r\n"))
        {
            if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(headerLine["Content-Length:".Length..].Trim());
            }
        }

        await stream.ReadExactlyAsync(new byte[contentLength], token);
    }

    private static async Task WriteAsync(NetworkStream stream, string text, CancellationToken token)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), token);
        await stream.FlushAsync(token);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopSource.CancelAsync();
        _listener.Stop();

        await _serveTask;

        _stopSource.Dispose();
    }
}
