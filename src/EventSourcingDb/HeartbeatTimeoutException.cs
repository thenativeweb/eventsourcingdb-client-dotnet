using System;

namespace EventSourcingDb;

public class HeartbeatTimeoutException(string? message) : Exception(message);
