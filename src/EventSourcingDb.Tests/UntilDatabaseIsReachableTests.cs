using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace EventSourcingDb.Tests;

public sealed class UntilDatabaseIsReachableTests : IDisposable
{
    private const ushort Port = 3000;
    private const string V2PingPath = "/api/v2/system/ping";
    private const string V1PingPath = "/api/v1/ping";

    // Both pings go out without an API token, so a request that carries one does not match these.
    private static readonly FakeRequest _v2Ping = new("GET", V2PingPath, null);
    private static readonly FakeRequest _v1Ping = new("GET", V1PingPath, null);

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(1) };

    [Fact]
    public async Task WaitsWhileDockerHasNotPublishedThePort()
    {
        var (container, _) = FakeContainer.Create();

        Assert.False(await CreateWait().UntilAsync(container));
    }

    [Theory]
    [InlineData(TestcontainersStates.Created)]
    [InlineData(TestcontainersStates.Dead)]
    [InlineData(TestcontainersStates.Undefined)]
    public async Task WaitsWhileTheContainerDoesNotRun(TestcontainersStates state)
    {
        await using var okServer = new FakeServerListener(new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var (container, fake) = FakeContainer.Create();
        fake.State = state;
        fake.MappedPorts = new Dictionary<ushort, ushort> { [Port] = (ushort)okServer.BaseUrl.Port };

        Assert.False(await CreateWait().UntilAsync(container));
    }

    [Fact]
    public async Task CountsOnlyTheStatus200()
    {
        await using var noContentServer = new FakeServerListener(new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        await using var okServer = new FakeServerListener(new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var (container, fake) = FakeContainer.Create();
        var wait = CreateWait();

        fake.MappedPorts = new Dictionary<ushort, ushort> { [Port] = (ushort)noContentServer.BaseUrl.Port };
        Assert.False(await wait.UntilAsync(container));

        fake.MappedPorts = new Dictionary<ushort, ushort> { [Port] = (ushort)okServer.BaseUrl.Port };
        Assert.True(await wait.UntilAsync(container));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task CountsTheV2PingAnswering200WithoutAskingTheV1Ping(HttpStatusCode v1Status)
    {
        // A v1 ping that answers 404 stands in for a server without the v1 API.
        var server = new FakeServer(request => new HttpResponseMessage(request.Path == V2PingPath ? HttpStatusCode.OK : v1Status));
        await using var listener = new FakeServerListener(server);

        Assert.True(await CreateWait().UntilAsync(CreateContainer(listener)));
        Assert.Equal([_v2Ping], server.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task AsksTheV1PingWithinTheSamePollIfTheServerDoesNotOfferTheV2Api(HttpStatusCode v2Status)
    {
        var server = new FakeServer(request => new HttpResponseMessage(request.Path == V2PingPath ? v2Status : HttpStatusCode.OK));
        await using var listener = new FakeServerListener(server);

        Assert.True(await CreateWait().UntilAsync(CreateContainer(listener)));
        Assert.Equal([_v2Ping, _v1Ping], server.Requests);
    }

    [Fact]
    public async Task DoesNotAskTheV1PingIfTheV2PingAnswersAnotherStatus()
    {
        // The v1 ping would count, so the attempt fails only if the wait does not ask it.
        var server = new FakeServer(request => new HttpResponseMessage(request.Path == V2PingPath ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        await using var listener = new FakeServerListener(server);

        Assert.False(await CreateWait().UntilAsync(CreateContainer(listener)));
        Assert.Equal([_v2Ping], server.Requests);
    }

    [Fact]
    public async Task DoesNotAskTheV1PingIfTheV2PingTimesOut()
    {
        // The v2 ping never answers, so it runs into the timeout of the HttpClient, and the v1
        // ping would count.
        var server = new FakeServer(async (request, token) =>
        {
            if (request.Path == V2PingPath)
            {
                await Task.Delay(Timeout.Infinite, token);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var listener = new FakeServerListener(server);

        Assert.False(await CreateWait().UntilAsync(CreateContainer(listener)));
        Assert.Equal([_v2Ping], server.Requests);
    }

    [Fact]
    public async Task StartsEveryPollWithTheV2Ping()
    {
        var v2Status = HttpStatusCode.NotFound;
        var server = new FakeServer(request => new HttpResponseMessage(request.Path == V2PingPath ? v2Status : HttpStatusCode.ServiceUnavailable));
        await using var listener = new FakeServerListener(server);
        var container = CreateContainer(listener);
        var wait = CreateWait();

        Assert.False(await wait.UntilAsync(container));

        v2Status = HttpStatusCode.OK;
        Assert.True(await wait.UntilAsync(container));

        Assert.Equal([_v2Ping, _v1Ping, _v2Ping], server.Requests);
    }

    [Fact]
    public async Task ReadsTheMappedPortAnewForEveryPoll()
    {
        await using var unavailableServer = new FakeServerListener(new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await using var okServer = new FakeServerListener(new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var (container, fake) = FakeContainer.Create();
        var wait = CreateWait();

        Assert.False(await wait.UntilAsync(container));

        fake.MappedPorts = new Dictionary<ushort, ushort> { [Port] = (ushort)unavailableServer.BaseUrl.Port };
        Assert.False(await wait.UntilAsync(container));

        fake.MappedPorts = new Dictionary<ushort, ushort> { [Port] = (ushort)okServer.BaseUrl.Port };
        Assert.True(await wait.UntilAsync(container));
    }

    [Fact]
    public async Task FailsWithTheLast20LinesOfTheWholeLogOnceTheContainerExited()
    {
        var (container, fake) = FakeContainer.Create();
        fake.State = TestcontainersStates.Exited;

        // Docker leaves out the zeros at the end of the fraction of a second, so a shorter time is
        // not necessarily an earlier one.
        var stdout = Enumerable.Range(0, 12)
            .Select(index => $"2026-10-19T10:00:{index * 2:00}Z stdout {index}\n");
        var stderr = Enumerable.Range(0, 12)
            .Select(index => $"2026-10-19T10:00:{index * 2:00}.5Z stderr {index}\n")
            .Append("2026-10-19T10:00:23.5Z stderr 12\n")
            .Append("2026-10-19T10:00:23.51Z stderr 13\n");
        fake.Logs = (string.Concat(stdout), string.Concat(stderr));

        var exception = await Assert.ThrowsAsync<ContainerExitedException>(() => CreateWait().UntilAsync(container));

        var expectedLines = Enumerable.Range(0, 12)
            .SelectMany(index => new[] { $"stdout {index}", $"stderr {index}" })
            .Append("stderr 12")
            .Append("stderr 13")
            .TakeLast(20);
        Assert.Equal(
            $"Failed to start the container, it exited before the database became reachable: {string.Join('\n', expectedLines)}",
            exception.Message
        );
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private UntilDatabaseIsReachable CreateWait()
    {
        return new UntilDatabaseIsReachable(Port, _httpClient, CancellationToken.None, CancellationToken.None);
    }

    private static IContainer CreateContainer(FakeServerListener listener)
    {
        var (container, fake) = FakeContainer.Create();
        fake.MappedPorts = new Dictionary<ushort, ushort> { [Port] = (ushort)listener.BaseUrl.Port };

        return container;
    }
}
