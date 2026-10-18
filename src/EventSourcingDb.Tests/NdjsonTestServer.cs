using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EventSourcingDb.Tests;

// Stands in for EventSourcingDB on a streaming endpoint. It answers the first request with the lines
// that sendLines sends, and then keeps the connection open without sending anything else, which is
// what a stalled connection looks like to the client.
internal sealed class NdjsonTestServer : IAsyncDisposable
{
    private readonly StallingTestServer _server;

    public NdjsonTestServer(Func<Func<string, Task>, CancellationToken, Task> sendLines)
    {
        _server = new StallingTestServer(async (write, token) =>
        {
            await write(
                "HTTP/1.1 200 OK\r\n" +
                "Server: EventSourcingDB/0.0.0\r\n" +
                "Content-Type: application/x-ndjson\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n"
            );

            await sendLines(line => write(Chunk(line + "\n")), token);
        });
    }

    public Uri BaseUrl => _server.BaseUrl;

    // Completes once the client has closed the connection.
    public Task ConnectionClosed => _server.ConnectionClosed;

    // Encodes text as one chunk of a response with chunked transfer encoding.
    public static string Chunk(string text)
    {
        return $"{Encoding.UTF8.GetByteCount(text):x}\r\n{text}\r\n";
    }

    public ValueTask DisposeAsync()
    {
        return _server.DisposeAsync();
    }
}
