using System.Text.Json;

namespace EventSourcingDb.Types;

internal static class EnvelopeExtensions
{
    public static Envelope ThrowIfNull(this Envelope? envelope, string rawJson)
    {
        if (envelope?.Type is null)
        {
            throw new InvalidValueException($"Failed to get the expected response, got null item from '{rawJson}'.");
        }

        return envelope;
    }

    public static void ThrowIfNotExpectedData(this Envelope envelope, string expectedType)
    {
        if (envelope.Data.ValueKind is not JsonValueKind.Object)
        {
            throw new InvalidValueException($"Expected item of type '{expectedType}' with an object as data, but got '{envelope.Data}'.");
        }
    }
}
