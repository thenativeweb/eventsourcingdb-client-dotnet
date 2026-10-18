using System;

namespace EventSourcingDb;

public class ConnectionStalledException(string? message) : TimeoutException(message);
