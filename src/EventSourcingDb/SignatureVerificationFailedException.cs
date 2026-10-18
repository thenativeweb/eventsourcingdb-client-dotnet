using System;

namespace EventSourcingDb;

public class SignatureVerificationFailedException(string? message) : Exception(message);
