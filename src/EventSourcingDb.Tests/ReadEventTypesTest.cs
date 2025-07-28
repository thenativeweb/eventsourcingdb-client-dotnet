using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class ReadEventTypesTest : IAsyncLifetime
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
    public async Task ReadsNoEventTypesIfTHeDatabaseIsEmpty()
    {
        var client = _container!.GetClient();
        var didReadEvenTypes = false;

        await foreach (var _ in client.ReadEventTypesAsync())
        {
            didReadEvenTypes = true;
        }

        Assert.False(didReadEvenTypes);
    }

    [Fact]
    public async Task ReadsAlleEventTypes()
    {
        var client = _container!.GetClient();

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

        await client.WriteEventsAsync([firstEvent, secondEvent]);

        var readEventTypes = new List<EventType>();

        await foreach (var eventType in client.ReadEventTypesAsync())
        {
            readEventTypes.Add(eventType);
        }

        Assert.Equal(2, readEventTypes.Count);
        Assert.Equal(readEventTypes[0], new EventType(
            Type: "io.eventsourcingdb.v1.test",
            IsPhantom: false,
            Schema: null
        ));
        Assert.Equal(readEventTypes[1], new EventType(
            Type: "io.eventsourcingdb.v2.test",
            IsPhantom: false,
            Schema: null
        ));
    }

    [Fact]
    public async Task SupportsReadingEventSchemas()
    {
        var client = _container!.GetClient();

        // TODO: simplify this once schema registration is supported by the client.
        var registerEventSchemaUrl = new Uri(_container.GetBaseUrl(), "/api/v1/register-event-schema");
        var eventSchema = new
        {
            Type = "object",
            Properties = new { value = new { type = "integer" } },
            Required = new[] { "value" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, registerEventSchemaUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _container.GetApiToken());
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                eventType = "io.eventsourcingdb.v1.test",
                schema = eventSchema
            }, _defaultSerializerOptions),
            Encoding.UTF8,
            "application/json"
        );
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var readEventTypes = new List<EventType>();

        await foreach (var eventType in client.ReadEventTypesAsync())
        {
            readEventTypes.Add(eventType);
        }

        Assert.Single(readEventTypes);
        Assert.Collection(readEventTypes, eventType =>
        {
            Assert.Equal("io.eventsourcingdb.v1.test", eventType.Type);
            Assert.True(eventType.IsPhantom);
            Assert.NotNull(eventType.Schema);

            var schema = eventType.Schema.Value;
            Assert.Equal(JsonValueKind.Object, schema.ValueKind);
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.Equal("integer", schema.GetProperty("properties").GetProperty("value").GetProperty("type").GetString());
            Assert.True(schema.TryGetProperty("required", out var required));
            Assert.Equal(JsonValueKind.Array, required.ValueKind);
            Assert.Equal(1, required.GetArrayLength());
            Assert.Equal("value", required[0].GetString());
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
