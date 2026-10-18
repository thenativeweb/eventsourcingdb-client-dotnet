using System;

namespace EventSourcingDb;

/// <summary>
/// Writing events or registering an event schema failed in a way that leaves open whether the
/// server stored it nonetheless, for example because the connection broke or stalled after the
/// request was sent. The exception that caused it is the <see cref="Exception.InnerException"/>.
/// Before repeating the write, find out whether it was stored.
/// </summary>
public class OutcomeUnknownException(Exception innerException)
    : Exception($"The outcome of the write is unknown, it may have been stored: {innerException.Message}", innerException);
