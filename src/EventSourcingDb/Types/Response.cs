namespace EventSourcingDb.Types;

internal record struct Response(string Type);

internal static class ResponseExtensions
{
    public static void ThrowNotExpectedType(this Response response, string expectedEventType)
    {
        if (string.IsNullOrEmpty(response.Type))
        {
            throw new InvalidValueException(
                $"Failed to get the expected response, got empty string, expected '{expectedEventType}'."
            );
        }

        if (response.Type != expectedEventType)
        {
            throw new InvalidValueException(
                $"Failed to get the expected response, got '{response.Type}' expected '{expectedEventType}'."
            );
        }
    }
}
