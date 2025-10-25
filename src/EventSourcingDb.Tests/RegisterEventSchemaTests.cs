using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public class RegisterEventSchemaTests : EventSourcingDbTests
{
    private const string SchemaJson =
        """
        {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "$id": "https://eventsourcingdb.io/schemas/test.json",
            "title": "TestEvent",
            "description": "Description for TestEvent",
            "type": "object",
            "properties": {
                "value": {
                    "type": "number"
                }
            },
            "required": [
                "value"
            ]
        }
        """;

    [Fact]
    public async Task RegistersAnEventSchema()
    {
        var client = Container!.GetClient();

        const string eventType = "io.eventsourcingdb.test";

        var schema = JsonDocument.Parse(SchemaJson).RootElement;

        var exception = await Record.ExceptionAsync(() => client.RegisterEventSchemaAsync(eventType, schema));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ReturnsErrorIfEventSchemaAlreadyRegistered()
    {
        var client = Container!.GetClient();

        const string eventType = "io.eventsourcingdb.test";

        var schema = JsonDocument.Parse(SchemaJson).RootElement;

        await client.RegisterEventSchemaAsync(eventType, schema);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.RegisterEventSchemaAsync(eventType, schema));
    }
}
