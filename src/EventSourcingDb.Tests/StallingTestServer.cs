using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb.Tests;

// Stands in for EventSourcingDB. It answers the first request with whatever respond writes, and
// then keeps the connection open without sending anything else, which is what a stalled
// connection looks like to the client. If respond writes nothing, not even the response headers
// arrive.
internal sealed class StallingTestServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly TaskCompletionSource _connectionClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _serveTask;

    public StallingTestServer(Func<Func<string, Task>, CancellationToken, Task> respond)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = new Uri($"http://127.0.0.1:{port}");

        _serveTask = ServeAsync(respond, _stopSource.Token);
    }

    public Uri BaseUrl { get; }

    // Completes once the client has closed the connection.
    public Task ConnectionClosed => _connectionClosed.Task;

    private async Task ServeAsync(Func<Func<string, Task>, CancellationToken, Task> respond, CancellationToken token)
    {
        try
        {
            using var tcpClient = await _listener.AcceptTcpClientAsync(token);
            await using var stream = tcpClient.GetStream();

            await ReadRequestAsync(stream, token);
            await respond(text => WriteAsync(stream, text, token), token);

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
