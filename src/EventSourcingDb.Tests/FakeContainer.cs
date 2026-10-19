using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;

namespace EventSourcingDb.Tests;

// Stands in for a container of Testcontainers, as far as UntilDatabaseIsReachable looks at it: its
// state, its host, the ports that Docker published for it, and its log. Every other member throws.
public class FakeContainer : DispatchProxy
{
    public TestcontainersStates State { get; set; } = TestcontainersStates.Running;

    public IReadOnlyDictionary<ushort, ushort> MappedPorts { get; set; } = new Dictionary<ushort, ushort>();

    public (string Stdout, string Stderr) Logs { get; set; } = (string.Empty, string.Empty);

    public int MappedPortsReadCount { get; private set; }

    public static (IContainer Container, FakeContainer Fake) Create()
    {
        var container = Create<IContainer, FakeContainer>();
        return (container, (FakeContainer)(object)container);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "get_State":
                return State;
            case "get_Hostname":
                return "127.0.0.1";
            case nameof(IContainer.GetMappedPublicPorts):
                MappedPortsReadCount++;
                return MappedPorts;
            case nameof(IContainer.GetLogsAsync):
                return Task.FromResult(Logs);
            default:
                throw new NotSupportedException($"FakeContainer does not support {targetMethod?.Name}.");
        }
    }
}
