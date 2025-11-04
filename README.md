# eventsourcingdb

The official .NET client SDK for [EventSourcingDB](https://www.eventsourcingdb.io) – a purpose-built database for event sourcing.

EventSourcingDB enables you to build and operate event-driven applications with native support for writing, reading, and observing events. This client SDK provides convenient access to its capabilities in .NET.

For more information on EventSourcingDB, see its [official documentation](https://docs.eventsourcingdb.io/).

This client SDK includes support for [Testcontainers](https://testcontainers.com/) to spin up EventSourcingDB instances in integration tests. For details, see [Using Testcontainers](#using-testcontainers).

## Getting Started

Install the client SDK:

```shell
dotnet add package EventSourcingDb
```

Import the `Client` class and create an instance by providing the URL of your EventSourcingDB instance and the API token to use:

```csharp
using EventSourcingDb;

var url = new Uri("http://localhost:3000");
var apiToken = "secret";

var client = new Client(url, apiToken);
```

Then call the `PingAsync` method to check whether the instance is reachable. If it is not, the method will throw an exception:

```csharp
await client.PingAsync();
```

Optionally, you might provide a `CancellationToken`.

*Note that `PingAsync` does not require authentication, so the call may succeed even if the API token is invalid.*

If you want to verify the API token, call `VerifyApiTokenAsync`. If the token is invalid, the function will throw an exception:

```csharp
await client.VerifyApiTokenAsync();
```

Optionally, you might provide a `CancellationToken`.

### Serializing and Deserializing

Basically, `System.Text.Json` is used for JSON serialization and deserialization.

You can override the default settings by either using JSON attributes on your types and properties or by providing your own `JsonSerializerOptions` when creating the `Client` both manually or via dependency injection.

### Dependency Injection

If you use Microsoft's dependency injection framework, you can call the `AddEventSourcingDb` extension method to register the `Client` as a scoped service:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.services.AddEventSourcingDb(builder.Configuration);
```

The client is configured according to your `IConfiguration` state, e.g. by setting environment variables or using the `EventSourcingDb` section in `appsettings.json`:

```json
{
  "EventSourcingDb": {
    "BaseUrl": "http://localhost:3000",
    "ApiToken": "secret"
  }
}
```

To configure the client at runtime, provide a configuration action:

```csharp
builder.Services.AddEventSourcingDb(builder.Configuration, options =>
{
    options.BaseUrl = new Uri("http://localhost:3000");
    options.ApiToken = "secret";
});
```

### Configuring the HttpClient via Dependency Injection

The `AddEventSourcingDb` method returns an `IHttpClientBuilder`, which allows you to configure the underlying `HttpClient` and add custom message handlers. This is useful for setting custom timeouts, implementing retry policies, adding logging or tracing handlers, and configuring advanced HTTP settings.

```csharp
builder.Services.AddEventSourcingDb(builder.Configuration)
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddHttpMessageHandler(() => new MyCustomRetryHandler());
```

This approach integrates seamlessly with .NET's `IHttpClientFactory`, ensuring proper connection pooling and lifecycle management.

### Using a Custom HttpClient

For advanced scenarios, you can inject a custom `HttpClient` into the `Client` constructor. This is useful when you need to:

- Configure retry policies with libraries like Polly
- Add custom headers for all requests
- Set custom timeouts
- Use custom message handlers for logging or tracing
- Configure advanced HTTP settings

When using a custom `HttpClient`, ensure you configure:

- **BaseAddress**: Set to your EventSourcingDB server URL
- **Authorization header**: Set to `Bearer {apiToken}`

For optimal connection pooling, use `SocketsHttpHandler` with a `PooledConnectionLifetime` of 2 minutes:

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using EventSourcingDb;

var handler = new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
};

var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("http://localhost:3000")
};
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

var client = new Client(httpClient);
```

This approach gives you full control over the HTTP client's behavior while maintaining compatibility with all EventSourcingDB client features.

## Writing Events

Call the `WriteEventsAsync` method and provide a collection of events. You do not have to set all event fields – some are automatically added by the server.

Specify `Source`, `Subject`, `Type`, and `Data` according to the [CloudEvents](https://docs.eventsourcingdb.io/fundamentals/cloud-events/) format.

For `Data`, you may provide any object that is serializable to JSON.

The method returns a list of written events, including the fields added by the server:

```csharp
var @event = new EventCandidate(
    Source: "https://library.eventsourcingdb.io",
    Subject: "/books/42",
    Type: "io.eventsourcingdb.library.book-acquired",
    Data: new {
        title = "2001 – A Space Odyssey",
        author = "Arthur C. Clarke",
        isbn = "978-0756906788"
    }
);

var writtenEvents = await client.WriteEventsAsync(new[] { @event });
```

*Optionally, you might provide a `CancellationToken`.*

### Using the `IsSubjectPristine` precondition

If you only want to write events in case a subject (such as `/books/42`) does not yet have any events, use the `IsSubjectPristinePrecondition`:

```csharp
var writtenEvents = await client.WriteEventsAsync(
    new[] { @event },
    new[] { Precondition.IsSubjectPristinePrecondition("/books/42") }
);
```

### Using the `IsSubjectPopulated` precondition

If you only want to write events in case a subject (such as `/books/42`) already has at least one event, use the `IsSubjectPopulatedPrecondition`:

```csharp
var writtenEvents = await client.WriteEventsAsync(
    new[] { @event },
    new[] { Precondition.IsSubjectPopulatedPrecondition("/books/42") }
);
```

### Using the `IsSubjectOnEventId` precondition

If you only want to write events in case the last event of a subject (such as `/books/42`) has a specific ID (e.g., `"0"`), use the `IsSubjectOnEventIdPrecondition`:

```csharp
var writtenEvents = await client.WriteEventsAsync(
    new[] { @event },
    new[] { Precondition.IsSubjectOnEventIdPrecondition("/books/42", "0") }
);
```

*Note that according to the CloudEvents standard, event IDs must be of type string.*

### Using the `IsEventQlQueryTrue` precondition

If you want to write events depending on an EventQL query, use the `IsEventQlQueryTruePrecondition`:

```csharp
var writtenEvents = await client.WriteEventsAsync(
    new[] { @event },
    new[] { Precondition.IsEventQlQueryTruePrecondition("FROM e IN events WHERE e.type == 'io.eventsourcingdb.library.book-borrowed' PROJECT INTO COUNT() < 10") }
);
```

*Note that the query must return a single row with a single value, which is interpreted as a boolean.*

## Reading Events

To read all events of a subject, call the `ReadEventsAsync` method and pass the subject and an options object. Set `Recursive` to `false` to ensure that only events of the given subject are returned, not events of nested subjects.

The method returns an async stream, which you can iterate over using `await foreach`:

```csharp
await foreach (var @event in client.ReadEventsAsync(
    "/books/42",
    new ReadEventsOptions(Recursive: false)))
{
    // Handle event
}
```

If an error occurs, the stream will terminate with an exception.

*Optionally, you might provide a `CancellationToken`.*

### Deserializing Event Data

Each event contains a `Data` property, which holds the event payload as JSON. To deserialize this payload into a strongly typed object, call `GetData<T>()`:

```csharp
var book = @event.GetData<BookAcquired>();
```

Alternatively, you can use the non-generic overload `GetData(Type)` to resolve the type at runtime:

```csharp
var type = typeof(BookAcquired);
var book = (BookAcquired)@event.GetData(type)!;
```

If you prefer to work directly with the JSON structure, access the `Data` property as a `JsonElement`:

```csharp
var title = @event.Data.GetProperty("title").GetString();
```

### Reading from subjects recursively

If you want to read not only all events of a subject, but also the events of all nested subjects, set `Recursive` to `true`:

```csharp
await foreach (var @event in client.ReadEventsAsync(
    "/books/42",
    new ReadEventsOptions(Recursive: true)))
{
    // ...
}
```

This also allows you to read *all* events ever written by using `/` as the subject.

### Reading in anti-chronological order

By default, events are read in chronological order. To read in anti-chronological order, use the `Order` option:

```csharp
await foreach (var @event in client.ReadEventsAsync(
    "/books/42",
    new ReadEventsOptions(
        Recursive: false,
        Order: Order.Antichronological)))
{
    // ...
}
```

*Note that you can also use `Order.Chronological` to explicitly enforce the default order.*

### Specifying bounds

If you only want to read a range of events, set the `LowerBound` and `UpperBound` options — either one of them or both:

```csharp
await foreach (var @event in client.ReadEventsAsync(
    "/books/42",
    new ReadEventsOptions(
        Recursive: false,
        LowerBound: new Bound("100", BoundType.Inclusive),
        UpperBound: new Bound("200", BoundType.Exclusive))))
{
    // ...
}
```

### Starting from the latest event of a given type

To start reading from the latest event of a specific type, set the `FromLatestEvent` option:

```csharp
await foreach (var @event in client.ReadEventsAsync(
    "/books/42",
    new ReadEventsOptions(
        Recursive: false,
        FromLatestEvent: new ReadFromLatestEvent(
            Subject: "/books/42",
            Type: "io.eventsourcingdb.library.book-borrowed",
            IfEventIsMissing: ReadIfEventIsMissing.ReadEverything))))
{
    // ...
}
```

*Note that `FromLatestEvent` and `LowerBound` cannot be used at the same time.*

## Running EventQL Queries

To run an EventQL query, call the `RunEventQlQueryAsync<TRow>` method and provide the query as an argument. The method returns an async stream, which you can iterate over using `await foreach`:

```csharp
await foreach (var row in client.RunEventQlQueryAsync<Event>(
    "FROM e IN events PROJECT INTO e"))
{
    // ...
}
```

Each row is deserialized automatically and returned as `TRow`, according to your projection. Ensure your projection matches the shape of `TRow`.

*Optionally, you might provide a `CancellationToken`.*

## Observing Events

To observe all future events of a subject, call the `ObserveEventsAsync` method and pass the subject and an options object. Set `Recursive` to `false` to observe only the events of the given subject.

The method returns an async stream:

```csharp
await foreach (var @event in client.ObserveEventsAsync(
    "/books/42",
    new ObserveEventsOptions(Recursive: false)))
{
    // Handle event
}
```

If an error occurs, the stream will terminate with an exception.

*Optionally, you might provide a `CancellationToken`.*

### Deserializing Event Data

Each event contains a `Data` property, which holds the event payload as JSON. To deserialize this payload into a strongly typed object, call `GetData<T>()`:

```csharp
var book = @event.GetData<BookAcquired>();
```

Alternatively, you can use the non-generic overload `GetData(Type)` to resolve the type at runtime:

```csharp
var type = typeof(BookAcquired);
var book = (BookAcquired)@event.GetData(type)!;
```

If you prefer to work directly with the JSON structure, access the `Data` property as a `JsonElement`:

```csharp
var title = @event.Data.GetProperty("title").GetString();
```

### Observing from subjects recursively

If you want to observe not only the events of a subject, but also events of all nested subjects, set `Recursive` to `true`:

```csharp
await foreach (var @event in client.ObserveEventsAsync(
    "/books/42",
    new ObserveEventsOptions(Recursive: true)))
{
    // ...
}
```

This also allows you to observe *all* events ever written by using `/` as the subject.

### Specifying bounds

If you want to start observing from a certain point, set the `LowerBound` option:

```csharp
await foreach (var @event in client.ObserveEventsAsync(
    "/books/42",
    new ObserveEventsOptions(
        Recursive: false,
        LowerBound: new Bound("100", BoundType.Inclusive))))
{
    // ...
}
```

### Starting from the latest event of a given type

To observe starting from the latest event of a specific type, use the `FromLatestEvent` option:

```csharp
await foreach (var @event in client.ObserveEventsAsync(
    "/books/42",
    new ObserveEventsOptions(
        Recursive: false,
        FromLatestEvent: new ObserveFromLatestEvent(
            Subject: "/books/42",
            Type: "io.eventsourcingdb.library.book-borrowed",
            IfEventIsMissing: ObserveIfEventIsMissing.ReadEverything))))
{
    // ...
}
```

*Note that `FromLatestEvent` and `LowerBound` cannot be used at the same time.*

## Registering an Event Schema

To register an event schema, call the `RegisterEventSchemaAsync` method and hand over an event type and the desired schema as a `JsonElement`:

```csharp
var schemaJson =
    """
    {
        "type": "object",
        "properties": {
            "title": { "type": "string" },
            "author": { "type": "string" },
            "isbn": { "type": "string" }
        },
        "required": [ "title", "author", "isbn" ],
        "additionalProperties": false
    }
    """;

var schema = JsonDocument.Parse(schemaJson).RootElement;

await client.RegisterEventSchemaAsync(
    "io.eventsourcingdb.library.book-acquired",
    schema
);
```

*Optionally, you might provide a `CancellationToken`.*

## Listing Event Types

To list all event types, call the `ReadEventTypesAsync` method. The method returns an async stream:

```csharp
await foreach (var eventType in client.ReadEventTypesAsync())
{
    // ...
}
```

## Listing a Specific Event Type

To list a specific event type, call the `ReadEventTypeAsync` method with the event type as an argument. The method returns the detailed event type, which includes the schema:

```csharp
var eventType = await client.ReadEventTypeAsync("io.eventsourcingdb.library.book-acquired");
```

## Listing Subjects

To list all subjects, call the `ReadSubjectsAsync` method with `/` as the base subject. The method returns an async stream:

```csharp
await foreach (var eventType in client.ReadSubjectsAsync("/"))
{
    // ...
}
```

If you only want to list subjects within a specific branch, provide the desired base subject instead:

```csharp
await foreach (var eventType in client.ReadSubjectsAsync("/books"))
{
    // ...
}
```

*Optionally, you might provide a `CancellationToken`.*

## Verifying an Event's Hash

To verify the integrity of an event, call the `VerifyHash` method on the event instance. This recomputes the event's hash locally and compares it to the hash stored in the event. If the hashes differ, the function returns an error:

```csharp
event.VerifyHash();
```

*Note that this only verifies the hash. If you also want to verify the signature, you can skip this step and call `VerifySignature` directly, which performs a hash verification internally.*

## Verifying an Event's Signature

To verify the authenticity of an event, call the `VerifySignature` method on the event instance. This requires the public key that matches the private key used for signing on the server.

The function first verifies the event's hash, and then checks the signature. If any verification step fails, it returns an error:

```csharp
var verificationKey = // an ed25519 public key

event.VerifySignature(verificationKey);
```

## Using Testcontainers

Import the `Container` class, create an instance, call the `StartAsync` method to run a test container, get a client, run your test code, and finally call the `StopAsync` method to stop the test container:

```csharp
using EventSourcingDb;

var container = new Container();
await container.StartAsync();

var client = container.GetClient();

// ...

await container.StopAsync();
```

Optionally, you might provide a `CancellationToken` to the `StartAsync` and `StopAsync` methods.

To check if the test container is running, call the `IsRunning` method:

```csharp
var isRunning = container.IsRunning();
```

### Configuring the Container Instance

By default, `Container` uses the `latest` tag of the official EventSourcingDB Docker image. To change that, call the `WithImageTag` method:

```csharp
var container = new Container()
    .WithImageTag("1.0.0");
```

Similarly, you can configure the port to use and the API token. Call the `WithPort` or the `WithApiToken` method respectively:

```csharp
var container = new Container()
    .WithPort(4000)
    .WithApiToken("secret");
```

If you want to sign events, call the `WithSigningKey` method. This generates a new signing and verification key pair inside the container:

```csharp
var container = new Container()
    .WithSigningKey();
```

You can retrieve the private key (for signing) and the public key (for verifying signatures) once the container has been started:

```csharp
var signingKey = container.GetSigningKey();
var verificationKey = container.GetVerificationKey();
```

The `signingKey` can be used when configuring the container to sign outgoing events. The `verificationKey` can be passed to `VerifySignature` when verifying events read from the database.

### Using a Custom HttpClient with Testcontainers

If you need to use a custom `HttpClient` with the test container (e.g., for custom timeouts, retry policies, or logging), you can pass it to the `GetClient` method:

```csharp
var container = new Container();
await container.StartAsync();

var httpClient = new HttpClient
{
    BaseAddress = container.GetBaseUrl(),
    Timeout = TimeSpan.FromSeconds(30)
};
httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", container.GetApiToken());

var client = container.GetClient(httpClient);
```

This gives you full control over the HTTP client configuration in your test scenarios.

### Configuring the Client Manually

In case you need to set up the client yourself, use the following methods to get details on the container:

- `GetHost()` returns the host name
- `GetMappedPort()` returns the port
- `GetBaseUrl()` returns the full URL of the container
- `GetApiToken()` returns the API token
