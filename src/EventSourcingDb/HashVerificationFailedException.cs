using System;

namespace EventSourcingDb;

public class HashVerificationFailedException(string? message) : Exception(message);
