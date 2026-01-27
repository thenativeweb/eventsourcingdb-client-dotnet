using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class ReadEventTypesTest : EventSourcingDbTests
{
    [Fact]
    public async Task ReadsNoEventTypesIfTheDatabaseIsEmpty()
    {
        var client = Container!.GetClient();
        var eventTypesRead = await client
            .ReadEventTypesAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(eventTypesRead);
    }

    [Fact]
    public async Task ReadsAllEventTypes()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.v1.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.v2.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent, secondEvent], token: TestContext.Current.CancellationToken);

        var eventTypesRead = await client
            .ReadEventTypesAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, eventTypesRead.Count);
        Assert.Equal(eventTypesRead[0], new EventType(
            Type: "io.eventsourcingdb.v1.test",
            IsPhantom: false,
            Schema: null
        ));
        Assert.Equal(eventTypesRead[1], new EventType(
            Type: "io.eventsourcingdb.v2.test",
            IsPhantom: false,
            Schema: null
        ));
    }

    [Fact]
    public async Task SupportsReadingEventSchemas()
    {
        var client = Container!.GetClient();

        const string schemaJson =
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
                ]
            }
            """;
        var schema = JsonDocument.Parse(schemaJson).RootElement;
        const string eventType = "io.eventsourcingdb.v1.test";
        await client.RegisterEventSchemaAsync(eventType, schema, TestContext.Current.CancellationToken);

        var eventTypesRead = await client
            .ReadEventTypesAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(eventTypesRead);
        Assert.Collection(eventTypesRead, type =>
        {
            Assert.Equal(eventType, type.Type);
            Assert.True(type.IsPhantom);
            Assert.NotNull(type.Schema);
            Assert.True(JsonElementComparer.Equals(type.Schema.Value, schema));
        });
    }

    private record struct EventData(int Value);
    private record struct RegisterEventSchemaRequest(
        string EventType,
        object Schema
    );
    private static readonly JsonSerializerOptions _defaultSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly HttpClient _httpClient = new HttpClient(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }
    );
}
