using System;

namespace EventSourcingDb;

// The container exited before the database in it became reachable, so another container would
// fail the same way. It tells this failure apart from the others only until ContainerStart throws
// an InvalidOperationException with the same message for it.
internal sealed class ContainerExitedException(string logTail)
    : Exception($"Failed to start the container, it exited before the database became reachable: {logTail}");
