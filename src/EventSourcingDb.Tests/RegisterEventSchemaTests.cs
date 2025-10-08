using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace EventSourcingDb.Tests;

public class RegisterEventSchemaTests : EventSourcingDbTests
{
    [Fact]
    public async Task RegistersAnEventSchema()
    {
        var client = Container!.GetClient();

        const string eventType = "io.eventsourcingdb.test";
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["value"] = new Dictionary<string, object>
                {
                    ["type"] = "number"
                }
            },
            ["required"] = new[] { "value" },
            ["additionalProperties"] = false
        };

        var exception = await Record.ExceptionAsync(() => client.RegisterEventSchemaAsync(eventType, schema));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ReturnsErrorIfEventSchemaAlreadyRegistered()
    {
        var client = Container!.GetClient();

        const string eventType = "io.eventsourcingdb.test";
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["value"] = new Dictionary<string, object>
                {
                    ["type"] = "number"
                }
            },
            ["required"] = new[] { "value" },
            ["additionalProperties"] = false
        };

        await client.RegisterEventSchemaAsync(eventType, schema);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.RegisterEventSchemaAsync(eventType, schema));
    }
}
