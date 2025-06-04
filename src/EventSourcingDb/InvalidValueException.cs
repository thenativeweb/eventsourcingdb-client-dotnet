using System;

namespace EventSourcingDb;

public class InvalidValueException(string? message) : Exception(message)
{
    internal string? _message = message;

    public override string Message
    {
        get
        {
            return _message ?? base.Message;
        }
    }
}
