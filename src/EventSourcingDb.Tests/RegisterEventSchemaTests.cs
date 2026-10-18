using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public class RegisterEventSchemaTests : EventSourcingDbTests
{
    private const string SchemaJson =
        """
        {
            "type": "object",
            "properties": {
                "value": {
                    "type": "number"
                }
            },
            "required": [
                "value"
            ],
            "additionalProperties": false
        }
        """;

    [Fact]
    public async Task RegistersAnEventSchema()
    {
        var client = Container!.GetClient();

        const string eventType = "io.eventsourcingdb.test";

        var schema = JsonDocument.Parse(SchemaJson).RootElement;

        var exception = await Record.ExceptionAsync(() => client.RegisterEventSchemaAsync(eventType, schema, TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ReturnsErrorIfEventSchemaAlreadyRegistered()
    {
        var client = Container!.GetClient();

        const string eventType = "io.eventsourcingdb.test";

        var schema = JsonDocument.Parse(SchemaJson).RootElement;

        var registerEventSchemaAsync = () => client.RegisterEventSchemaAsync(eventType, schema, TestContext.Current.CancellationToken);

        await registerEventSchemaAsync();

        var exception = await Assert.ThrowsAsync<DbApiException>(registerEventSchemaAsync);

        Assert.Equal(ErrorCodes.EventSchemaAlreadyRegistered, exception.Code);
    }
}
