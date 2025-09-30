using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class CustomSerializationOptionsTests : EventSourcingDbTests
{
    [Fact]
    public async Task UsesCustomSerializationOptionsToControlSerialization()
    {
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        var client = Container!.GetClient(serializerOptions);

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(MyEnum.Foo)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate]);

        var writtenEvent = writtenEvents[0];
        var eventDataJsonString = writtenEvent.Data.ToString();

        Assert.Equal("{\"value\":\"Foo\"}", eventDataJsonString);

        var deserializedEventData = writtenEvent.GetData<EventData>();

        Assert.Equal(eventCandidate.Data, deserializedEventData);
    }

    private record struct EventData(MyEnum Value);

    private enum MyEnum
    {
        Foo,
        Bar
    }
}
