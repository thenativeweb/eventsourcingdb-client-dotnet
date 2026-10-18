# eventsourcingdb

The official .NET client SDK for [EventSourcingDB](https://www.eventsourcingdb.io) – a purpose-built database for event sourcing.

EventSourcingDB enables you to build and operate event-driven applications with native support for writing, reading, and observing events. This client SDK provides convenient access to its capabilities in .NET.

For more information on EventSourcingDB, see its [official documentation](https://docs.eventsourcingdb.io/).

This client SDK includes support for [Testcontainers](https://testcontainers.com/) to spin up EventSourcingDB instances in integration tests. For details, see [Using Testcontainers](#using-testcontainers).

If you are upgrading from version 1.x of this client SDK, see [Upgrading from 1.x](#upgrading-from-1x).

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

### Choosing the API

The client SDK works with servers that offer the v2 API of EventSourcingDB, which EventSourcingDB offers since version 1.3, and with servers that only offer the v1 API. Creating a client does not make any request. Instead, the first request of a client finds out which API the server offers, by sending a `GET` request to `/api/v2/system/ping`, and the client remembers the answer for all of its following requests. If this fails, for example because the server is not reachable, the first request fails with that error, and the next request tries again.

*Note that with dependency injection, every resolved `IClient` is a client of its own, which finds out the API on its own first request.*

On servers that offer the v2 API, all reading requests use the HTTP method `QUERY` ([RFC 10008](https://www.rfc-editor.org/rfc/rfc10008)). If there is a proxy, a load balancer, or a firewall in front of EventSourcingDB, it has to let `QUERY` requests through. There is no fallback to `POST`.

### Handling Errors

Every error that EventSourcingDB reports results in a `DbApiException`, whether the server refuses a request with an HTTP status code or reports the error within its response. It offers:

- `Code`, the code of the error exactly as the server sends it, which is the URL of the page that documents the error, such as `https://docs.eventsourcingdb.io/errors/precondition-violated`. It is `null` for errors without a code.
- `Description`, the description of the error.
- `Details`, the details of the error as a `JsonElement`, or `null` for errors without details.
- `Status`, the HTTP status of the response, which is `200` for an error that the server reports within its response.
- `RetryAfter`, for a status of `429`, how long to wait before trying again, as the `Retry-After` header tells it, or `null`.

The `ErrorCodes` class contains a constant for every code, so you can tell errors apart without comparing strings yourself:

```csharp
try
{
    await client.WriteEventsAsync(
        new[] { @event },
        new[] { Precondition.IsSubjectPristinePrecondition("/books/42") }
    );
}
catch (DbApiException exception) when (exception.Code == ErrorCodes.PreconditionViolated)
{
    // The precondition does not hold.
}
```

Servers that only offer the v1 API report errors without a code. For those that clearly match an error of the v2 API, the client SDK sets the code of the v2 API, for example `ErrorCodes.PreconditionViolated` for a precondition that does not hold, `ErrorCodes.EventSchemaViolated` for an event that does not match its schema, `ErrorCodes.EventSchemaAlreadyRegistered`, `ErrorCodes.EventTypeUnknown`, `ErrorCodes.StoreReadOnly`, and `ErrorCodes.LicenseInvalid`. All other errors of the v1 API have no code.

The client SDK never repeats a request on its own, not even after a status of `429`.

#### Knowing Whether a Write Was Stored

EventSourcingDB never stops a write halfway, so a write whose response is lost may have been stored nonetheless. If `WriteEventsAsync` or `RegisterEventSchemaAsync` fails in a way that leaves open whether the server stored the write, it fails with an `OutcomeUnknownException`, whose `InnerException` is the cause. This happens, for example, if the connection stalls or breaks after the request was sent, if you cancel the request after it was sent, if the response ends before it is complete, or if the server fails unexpectedly, such as with a status of `500`. Before repeating such a write, find out whether it was stored, for example by reading the events of its subject.

A write is certainly not stored, and fails with the error itself, only if the connection could not be established at all (for example, because the host name could not be resolved, the connection was refused, or TLS failed), or if the server explicitly refused the write, for example because a precondition does not hold:

```csharp
try
{
    await client.WriteEventsAsync(new[] { @event });
}
catch (OutcomeUnknownException)
{
    // The events may have been written. Check before writing them again.
}
```

### Configuring the Stall Timeout

If the connection to EventSourcingDB stalls, for example behind a proxy that keeps it open but no longer passes anything on, a request fails once nothing at all has arrived from the server for the stall timeout. This applies to every request, from sending it until its end, including connecting and waiting for the response. Everything that arrives starts the time anew, so a long stream that keeps receiving events or heartbeats is never cut off.

The stall timeout defaults to 30 seconds. To change it, pass `ClientOptions` when creating the client:

```csharp
var client = new Client(url, apiToken, new ClientOptions
{
    StallTimeout = TimeSpan.FromSeconds(10)
});
```

When a request stalls, it fails with a `ConnectionStalledException`, whether while waiting for the response or while reading it. Since a stalled write may have been stored nonetheless, `WriteEventsAsync` and `RegisterEventSchemaAsync` wrap it in an [`OutcomeUnknownException`](#knowing-whether-a-write-was-stored).

`ConnectionStalledException` derives from `TimeoutException`. A `CancellationToken` that you pass to a method can still cancel a request earlier.

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

To change the [stall timeout](#configuring-the-stall-timeout), set `StallTimeout`, either in the configuration (e.g. `"StallTimeout": "00:00:10"`) or in the configuration action (e.g. `options.StallTimeout = TimeSpan.FromSeconds(10);`).

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

*Note that the client bounds every request by its [stall timeout](#configuring-the-stall-timeout), so it turns off the default `Timeout` of 100 seconds of the `HttpClient`. A `Timeout` that you set here, or for all clients via `ConfigureHttpClientDefaults`, applies, but only until the response headers arrive.*

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

*Note that the `Timeout` of a custom `HttpClient` only applies until the response headers arrive. Every request is also bounded by the [stall timeout](#configuring-the-stall-timeout), which defaults to 30 seconds.*

To change the stall timeout, pass `ClientOptions` as well. This constructor requires all of its parameters, so pass `null` for the `JsonSerializerOptions` and the logger if you do not need them:

```csharp
var client = new Client(httpClient, new ClientOptions
{
    StallTimeout = TimeSpan.FromSeconds(10)
}, null, null);
```

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

If an error occurs, the stream will terminate with an exception, a [`DbApiException`](#handling-errors) for an error that the server reports. On servers that offer the v2 API, a stream that breaks off before EventSourcingDB has sent all events terminates with an `HttpIOException`. The same applies to `ReadSubjectsAsync`, `ReadEventTypesAsync`, and `RunEventQlQueryAsync`.

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

EventSourcingDB sends a heartbeat every second while there are no rows to send. If nothing arrives for the [stall timeout](#configuring-the-stall-timeout), which defaults to 30 seconds, the connection has stalled, and the stream will terminate with a `ConnectionStalledException`.

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

EventSourcingDB sends a heartbeat every second while there are no new events. If nothing arrives for the [stall timeout](#configuring-the-stall-timeout), which defaults to 30 seconds, the connection has stalled, and the stream will terminate with a `ConnectionStalledException`.

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

If a schema is already registered for the event type, the method throws a `DbApiException` with the code `ErrorCodes.EventSchemaAlreadyRegistered`.

*Optionally, you might provide a `CancellationToken`.*

## Listing Event Types

To list all event types, call the `ReadEventTypesAsync` method. The method returns an async stream of `EventType` instances, each with the event type as `Type`, whether there are events of the type as `HasEvents`, and the registered schema, if any, as `Schema`:

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

If the event type has neither events nor a registered schema, the method throws a `DbApiException` with the code `ErrorCodes.EventTypeUnknown`.

## Listing Subjects

To list subjects, call the `ReadSubjectsAsync` method with a subject and an options object, in which you must decide whether to read recursively. To list all subjects, use `/` as the subject and set `Recursive` to `true`. The method returns an async stream:

```csharp
await foreach (var subject in client.ReadSubjectsAsync(
    "/",
    new ReadSubjectsOptions(Recursive: true)))
{
    // ...
}
```

If you only want to list the subjects within a specific branch, provide the subject of the branch instead:

```csharp
await foreach (var subject in client.ReadSubjectsAsync(
    "/books",
    new ReadSubjectsOptions(Recursive: true)))
{
    // ...
}
```

With `Recursive` set to `false`, the stream contains only the given subject itself, if it exists:

```csharp
await foreach (var subject in client.ReadSubjectsAsync(
    "/books/42",
    new ReadSubjectsOptions(Recursive: false)))
{
    // ...
}
```

*Optionally, you might provide a `CancellationToken`.*

## Verifying an Event's Hash

To verify the integrity of an event, call the `VerifyHash` method on the event instance. This recomputes the event's hash locally and compares it to the hash stored in the event. If the hashes differ, the method throws a `HashVerificationFailedException`:

```csharp
event.VerifyHash();
```

The hash of an event comes in one of two versions. Since version 1.3, EventSourcingDB writes hashes of version 2, which start with the prefix `esdb:hash:v2:`. Events written before keep their hash of version 1, which has no prefix. `VerifyHash` supports both versions. If the hash is of a version this client SDK does not know, the event cannot be verified, and the method throws an `UnsupportedHashVersionException` that names the version.

*Note that this only verifies the hash. If you also want to verify the signature, you can skip this step and call `VerifySignature` directly, which performs a hash verification internally.*

## Verifying an Event's Signature

To verify the authenticity of an event, call the `VerifySignature` method on the event instance. This requires the public key that matches the private key used for signing on the server.

The method first verifies the event's hash in the same way as `VerifyHash`, and then checks the signature. If the hash does not match, it throws a `HashVerificationFailedException`, and if the signature is invalid, a `SignatureVerificationFailedException`. If the event is not signed at all, it throws an `Exception`:

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

You can retrieve the private key (for signing) and the public key (for verifying signatures) once the container has been started. If the container was started without a signing key, both methods throw an `InvalidOperationException`:

```csharp
var signingKey = container.GetSigningKey();
var verificationKey = container.GetVerificationKey();
```

The `signingKey` is the private key EventSourcingDB signs events with, in the PKCS#8 format. The `verificationKey` can be passed to `VerifySignature` when verifying events read from the database.

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

## Upgrading from 1.x

Version 2.0 of this client SDK speaks the v2 API of EventSourcingDB as well as the v1 API, and [chooses](#choosing-the-api) between them on the first request of a client. On servers that offer the v2 API, reading requests use the HTTP method `QUERY`, which a proxy in front of EventSourcingDB has to let through. Besides that, the following changes break code written for version 1.x:

- **`ReadSubjectsAsync` takes a subject and required options.** The parameter `baseSubject` is now `subject`, and you have to decide whether to read recursively. Version 1.x always read recursively.

  ```csharp
  // Before
  client.ReadSubjectsAsync("/books");

  // After
  client.ReadSubjectsAsync("/books", new ReadSubjectsOptions(Recursive: true));
  ```

- **`EventType` tells whether an event type has events, instead of whether it is a phantom.** `IsPhantom` is replaced by its opposite, `HasEvents`, also in the constructor. When serialized to JSON, the event type is now named `type` instead of `eventType`.

  ```csharp
  // Before
  if (eventType.IsPhantom) { /* ... */ }

  // After
  if (!eventType.HasEvents) { /* ... */ }
  ```

- **Errors that the server reports are `DbApiException`s.** Version 1.x threw an `HttpRequestException` with the status as `StatusCode` and the text within its message, and a plain `Exception` for an error within a stream. See [Handling Errors](#handling-errors).

  ```csharp
  // Before
  catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict) { /* ... */ }

  // After
  catch (DbApiException exception) when (exception.Code == ErrorCodes.PreconditionViolated) { /* ... */ }
  ```

- **A write that may have been stored fails with an `OutcomeUnknownException`.** If `WriteEventsAsync` or `RegisterEventSchemaAsync` fails in a way that leaves open whether the write was stored, the original exception, such as a `ConnectionStalledException`, an `HttpRequestException`, or the `OperationCanceledException` of a cancellation after the request was sent, is now the `InnerException` of an `OutcomeUnknownException`. See [Knowing Whether a Write Was Stored](#knowing-whether-a-write-was-stored).

  ```csharp
  // Before
  catch (OperationCanceledException) { /* ... */ }

  // After
  catch (OutcomeUnknownException exception) when (exception.InnerException is OperationCanceledException) { /* ... */ }
  ```

- **`HeartbeatTimeoutException` is gone.** A stalled stream of `ObserveEventsAsync` or `RunEventQlQueryAsync` now fails with a `ConnectionStalledException`, as every other stalled request does.

  ```csharp
  // Before
  catch (HeartbeatTimeoutException) { /* ... */ }

  // After
  catch (ConnectionStalledException) { /* ... */ }
  ```

- **A stall always fails with a `ConnectionStalledException`.** Version 1.x threw a `TaskCanceledException` with a `ConnectionStalledException` as its `InnerException` if the response did not begin, or if a response that is no stream stalled.

  ```csharp
  // Before
  catch (TaskCanceledException exception) when (exception.InnerException is ConnectionStalledException) { /* ... */ }

  // After
  catch (ConnectionStalledException) { /* ... */ }
  ```

- **`ReadEventTypeAsync` and `RegisterEventSchemaAsync` check that the response comes from EventSourcingDB**, as all other methods already did, and otherwise throw an `InvalidOperationException`.
