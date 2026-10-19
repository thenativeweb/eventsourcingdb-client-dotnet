using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Configurations;

namespace EventSourcingDb.Tests;

// Stands between Testcontainers and Docker, and holds the first requests to create a container
// for a while before it passes them on, as a Docker that is slow to create a container does. It
// passes such a request on even once the client stopped waiting for it, as Docker creates a
// container that it was asked for, and it keeps the connection to Docker open for that.
internal sealed class DockerProxy : IAsyncDisposable
{
    private static readonly byte[] _createPath = "/containers/create"u8.ToArray();

    private readonly TimeSpan _holdTime;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly ConcurrentQueue<TimeSpan> _createArrivals = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Task _acceptTask;
    private int _holdCount;

    public DockerProxy(TimeSpan holdTime, int holdCount)
    {
        _holdTime = holdTime;
        _holdCount = holdCount;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        Endpoint = new Uri($"tcp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");

        _acceptTask = AcceptAsync(_stopSource.Token);
    }

    public Uri Endpoint { get; }

    // When each request to create a container arrived, counted from creating the proxy.
    public IReadOnlyList<TimeSpan> CreateArrivals => _createArrivals.ToArray();

    // Returns once the given number of requests to create a container arrived.
    public async Task WaitForCreatesAsync(int count, CancellationToken token)
    {
        using var guardSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        guardSource.CancelAfter(TimeSpan.FromSeconds(30));

        while (_createArrivals.Count < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), guardSource.Token);
        }
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _connections.Add(ServeAsync(client, token));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Ignored, the proxy is being disposed
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            await IgnoreDisconnectsAsync(PassAsync(client, token));
        }
    }

    private async Task PassAsync(TcpClient client, CancellationToken token)
    {
        using var docker = await ConnectToDockerAsync(token);
        await using var clientStream = client.GetStream();
        await using var dockerStream = new NetworkStream(docker, ownsSocket: false);

        // Once the client is gone, the connection to Docker stays open until Docker answered, so
        // that Docker finishes what it was asked for. Once Docker closes the connection, which it
        // does after each answer, the client learns that the answer is complete.
        await Task.WhenAll(
            IgnoreDisconnectsAsync(PassToDockerAsync(clientStream, dockerStream, token)),
            IgnoreDisconnectsAsync(PassToClientAsync(dockerStream, clientStream, client.Client, token))
        );
    }

    private static async Task PassToClientAsync(Stream dockerStream, Stream clientStream, Socket clientSocket, CancellationToken token)
    {
        await dockerStream.CopyToAsync(clientStream, token);
        clientSocket.Shutdown(SocketShutdown.Send);
    }

    private async Task PassToDockerAsync(Stream clientStream, Stream dockerStream, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        var tail = Array.Empty<byte>();

        while (true)
        {
            var count = await clientStream.ReadAsync(buffer, token);
            if (count == 0)
            {
                return;
            }

            // A path may be split across two reads, so the end of the last read is searched, too.
            var searched = tail.Concat(buffer.Take(count)).ToArray();
            tail = searched.TakeLast(_createPath.Length - 1).ToArray();

            if (searched.AsSpan().IndexOf(_createPath) >= 0)
            {
                _createArrivals.Enqueue(_stopwatch.Elapsed);

                if (Interlocked.Decrement(ref _holdCount) >= 0)
                {
                    await Task.Delay(_holdTime, token);
                }
            }

            await dockerStream.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    private static async Task IgnoreDisconnectsAsync(Task pumping)
    {
        try
        {
            await pumping;
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Ignored, one side has closed the connection, or the proxy is being disposed
        }
    }

    private static async Task<Socket> ConnectToDockerAsync(CancellationToken token)
    {
        var endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig.Endpoint;

        if (endpoint.Scheme == "unix")
        {
            var unixSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await unixSocket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.AbsolutePath), token);
            return unixSocket;
        }

        var tcpSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await tcpSocket.ConnectAsync(endpoint.Host, endpoint.Port, token);
        return tcpSocket;
    }

    public async ValueTask DisposeAsync()
    {
        await _stopSource.CancelAsync();
        _listener.Stop();

        await _acceptTask;
        await Task.WhenAll(_connections);

        _stopSource.Dispose();
    }
}
