using System;

namespace EventSourcingDb;

public class ValueOutOfRangeException : Exception
{
    // Allow the message to mutate to avoid re-throwing and losing the StackTrace to an inner exception.
    internal string? _message;

    /// <summary>
    /// Creates a new exception object to relay error information to the user.
    /// </summary>
    /// <param name="message">The context specific error message.</param>
    public ValueOutOfRangeException(string? message) : base(message)
    {
        _message = message;
    }

    /// <summary>
    /// Gets a message that describes the current exception.
    /// </summary>
    public override string Message
    {
        get
        {
            return _message ?? base.Message;
        }
    }
}
