using System.Text.Json;

namespace EventSourcingDb.Types;

// An event type as the v1 API sends it, which tells whether it is a phantom, that is, whether it
// has no events.
internal record V1EventType(
    string EventType,
    bool IsPhantom,
    JsonElement? Schema
)
{
    public EventType ToEventType() => new EventType(EventType, !IsPhantom, Schema);
}
