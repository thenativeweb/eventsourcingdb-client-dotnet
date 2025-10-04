using System.Text.Json;

namespace EventSourcingDb.Types;

internal record Line(string Type, JsonElement Payload);

internal static class LineExtensions
{
    public static Line ThrowIfNull(this Line? line, string rawJson)
    {
        if (line?.Type is null)
        {
            throw new InvalidValueException($"Failed to get the expected response, got null line from '{rawJson}'.");
        }

        return line;
    }

    public static void ThrowIfNotExpectedPayload(this Line line, string expectedPayloadType)
    {
        if (line.Payload.ValueKind is not JsonValueKind.Object)
        {
            throw new InvalidValueException($"Expected line of type '{expectedPayloadType}', but got '{line.Type}'.");
        }
    }

    public static void ThrowIfNotExpectedError(this Line line)
    {
        if (line.Payload.ValueKind is not JsonValueKind.String)
        {
            throw new InvalidValueException($"Received line of type 'error', but payload is not a string: '{line.Payload}'.");
        }
    }
}
