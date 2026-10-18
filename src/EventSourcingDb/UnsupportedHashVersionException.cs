using System;

namespace EventSourcingDb;

public class UnsupportedHashVersionException(string? message) : Exception(message);
