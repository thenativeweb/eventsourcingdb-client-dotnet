# eventsourcingdb-client-dotnet

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

### Using Testcontainers

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
