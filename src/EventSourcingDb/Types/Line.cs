using System.Text.Json;

namespace EventSourcingDb.Types;

// An item of a stream of the v1 API.
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

    // The v1 API describes an error within a stream as {"error": "..."}.
    public static string GetErrorDescription(this Line line)
    {
        if (line.Payload.ValueKind is JsonValueKind.Object &&
            line.Payload.TryGetProperty("error", out var error) &&
            error.ValueKind is JsonValueKind.String)
        {
            return error.GetString()!;
        }

        throw new InvalidValueException($"Received line of type 'error', but its payload holds no error: '{line.Payload}'.");
    }
}
