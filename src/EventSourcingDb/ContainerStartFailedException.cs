using System;

namespace EventSourcingDb;

/// <summary>
/// Starting a test container failed, since the database did not become reachable in any of the
/// containers, since the container exited before, or since Docker failed. In the latter case, the
/// error of Docker is the <see cref="Exception.InnerException"/>.
/// </summary>
public class ContainerStartFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
