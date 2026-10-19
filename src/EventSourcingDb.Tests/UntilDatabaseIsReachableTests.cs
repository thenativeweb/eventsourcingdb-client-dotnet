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

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(1) };

    [Fact]
    public async Task WaitsWhileDockerHasNotPublishedThePort()
    {
        var (container, _) = FakeContainer.Create();

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

    [Fact]
    public async Task ReadsTheMappedPortAnewForEveryPing()
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

        var exception = await Assert.ThrowsAsync<ContainerStartFailedException>(() => CreateWait().UntilAsync(container));

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
}
