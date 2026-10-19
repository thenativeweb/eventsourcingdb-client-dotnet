using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace EventSourcingDb;

// Waits until the database in a container answers a ping with the status 200, which is the only
// status that counts. Testcontainers inspects the container anew before every poll, and the
// mapped port is read anew for every ping, since Docker sometimes publishes it only a while after
// it started the container. Once the container has exited, it fails with the end of its log.
internal sealed class UntilDatabaseIsReachable(
    ushort port,
    HttpClient httpClient,
    CancellationToken attemptToken,
    CancellationToken token
) : IWaitUntil
{
    private const int LogTailLength = 20;

    public async Task<bool> UntilAsync(IContainer container)
    {
        // Docker marks a container as dead while it removes it, which is no exit of its own.
        if (container.State is TestcontainersStates.Exited)
        {
            // The container exited before the startup timeout ran out, so its log is read even if
            // the attempt ends meanwhile.
            var logTail = await ReadLogTailAsync(container, token).ConfigureAwait(false);
            throw new ContainerStartFailedException($"Failed to start the container, it exited before the database became reachable: {logTail}");
        }

        // A container that does not run, for example since Docker is removing it, has no published
        // port, and inspecting it fails next time once it is gone.
        if (container.State is not TestcontainersStates.Running
            || !container.GetMappedPublicPorts().TryGetValue(port, out var mappedPort))
        {
            return false;
        }

        try
        {
            using var response = await httpClient
                .GetAsync(new Uri($"http://{container.Hostname}:{mappedPort}/api/v1/ping"), attemptToken)
                .ConfigureAwait(false);

            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException)
        {
            // While the database starts, nothing may listen on the port yet, which is a reason to
            // ask again, not an error.
            return false;
        }
        catch (TaskCanceledException) when (!attemptToken.IsCancellationRequested)
        {
            // The ping timed out.
            return false;
        }
    }

    // Docker hands out what the container wrote to stdout and to stderr apart from each other, so
    // the lines of both are put back into the order in which they arrived, by their timestamps.
    private static async Task<string> ReadLogTailAsync(IContainer container, CancellationToken token)
    {
        var (stdout, stderr) = await container.GetLogsAsync(timestampsEnabled: true, ct: token).ConfigureAwait(false);

        var lines = ReadLines(stdout)
            .Concat(ReadLines(stderr))
            .OrderBy(line => line.Time, StringComparer.Ordinal)
            .Select(line => line.Text)
            .TakeLast(LogTailLength);

        return string.Join('\n', lines);
    }

    private static IEnumerable<(string Time, string Text)> ReadLines(string log)
    {
        foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = line.IndexOf(' ', StringComparison.Ordinal);
            var time = separatorIndex < 0 ? line : line[..separatorIndex];
            var text = separatorIndex < 0 ? string.Empty : line[(separatorIndex + 1)..];

            yield return (GetSortableTime(time), text.TrimEnd('\r'));
        }
    }

    // Docker writes the time in UTC with up to nine digits for the fraction of the second, and
    // leaves out the zeros at its end, so the fraction is padded to compare the times as text.
    private static string GetSortableTime(string time)
    {
        var withoutZone = time.TrimEnd('Z');
        var separatorIndex = withoutZone.IndexOf('.', StringComparison.Ordinal);

        return separatorIndex < 0
            ? $"{withoutZone}.000000000"
            : $"{withoutZone[..separatorIndex]}.{withoutZone[(separatorIndex + 1)..].PadRight(9, '0')}";
    }
}
