using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class CustomSerializationOptionsTests : IAsyncLifetime
{
    private Container? _container;

    public async Task InitializeAsync()
    {
        var imageVersion = DockerfileHelper.GetImageVersionFromDockerfile();
        _container = new Container().WithImageTag(imageVersion);
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
    }

    [Fact]
    public async Task UsesCustomSerializationOptionsForWritingData()
    {
        var client = _container!.GetClient();

        // How to use custom serialization options with client (maybe constructor overload)?
        var customSerializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        var eventCandidate = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: new EventData(MyEnum.Value1)
        );

        var writtenEvents = await client.WriteEventsAsync([eventCandidate]);

        var writtenEvent = writtenEvents[0];
        var eventDataJsonString = writtenEvent.Data.ToString();

        Assert.Equal("{\"value\":\"Value1\"}", eventDataJsonString);

        var deserializedEventData = writtenEvent.GetData<EventData>();

        Assert.Equal(eventCandidate.Data, deserializedEventData);
    }

    private record struct EventData(MyEnum Value);

    private enum MyEnum
    {
        Value1,
        Value2
    }
}
