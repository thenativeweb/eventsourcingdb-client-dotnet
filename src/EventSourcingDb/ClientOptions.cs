using System;

namespace EventSourcingDb;

public class ClientOptions
{
    internal static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a request waits for anything to arrive from the server before it fails, from
    /// sending the request until its end. Everything that arrives restarts this time, so a stream
    /// that keeps receiving events or heartbeats stays open. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan StallTimeout { get; set; } = DefaultStallTimeout;
}
