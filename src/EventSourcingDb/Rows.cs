using System.Text.Json;
using EventSourcingDb.Types;

namespace EventSourcingDb;

internal static class Rows
{
    // Deserializes a row of an EventQL query into the type that the caller asks for. An event is
    // deserialized as such, so that its data can be deserialized with the options of the caller.
    public static TRow? Deserialize<TRow>(JsonElement row, JsonSerializerOptions dataSerializerOptions)
    {
        try
        {
            if (typeof(TRow) != typeof(Event))
            {
                return row.Deserialize<TRow>(ServerConnection.DefaultSerializerOptions);
            }

            var cloudEvent = row.Deserialize<CloudEvent>(ServerConnection.DefaultSerializerOptions);
            if (cloudEvent is null)
            {
                throw new InvalidValueException($"Failed to get the expected response, unable to deserialize '{row}' into cloud event.");
            }

            // At design time, there is no type conversion between TRow and Event, so we need to cast to object first
            return (TRow)(object)new Event(cloudEvent, dataSerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidValueException($"Failed to deserialize query result into type '{typeof(TRow).Name}': {ex.Message}");
        }
    }
}
