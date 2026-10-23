using System;

namespace EventSourcingDb;

/// <summary>
/// The server does not offer what a request needs, such as the v2 API, which a recursive
/// precondition needs. The client SDK finds that out before it sends the request, so a write
/// that fails with it is certainly not stored.
/// </summary>
public class IncompatibleServerException(string? message) : Exception(message);
