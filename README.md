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

## Serializing and Deserializing

System.Text.Json is used for JSON serialization and deserialization.

You can override the default settings by either using JSON attributes on your types and properties or by providing your own `JsonSerializerOptions` when creating the `Client`.

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

#### Deserializing Event Data

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
await foreach (var row in client.RunEventQlQueryAsync(
    "FROM e IN events PROJECT INTO { count: COUNT() }"))
{
    // ...
}
```

Each row is deserialized automatically and returned as `TRow`, according to your projection.

When projecting into events ("... PROJECT into e"), use `Event` as generic type and obtain the deserialized data as described below.

*Ensure your projection matches the shape of `TRow`.*

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

#### Deserializing Event Data

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

#### Configuring the Container Instance

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

#### Configuring the Client Manually

In case you need to set up the client yourself, use the following methods to get details on the container:

- `GetHost()` returns the host name
- `GetMappedPort()` returns the port
- `GetBaseUrl()` returns the full URL of the container
- `GetApiToken()` returns the API token
