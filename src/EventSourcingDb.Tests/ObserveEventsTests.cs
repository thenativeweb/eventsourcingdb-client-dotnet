using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingDb.Types;
using Xunit;

namespace EventSourcingDb.Tests;

public class ObserveEventsTests(ITestOutputHelper testOutputHelper) : EventSourcingDbTests
{
    [Fact]
    public async Task ObserveNoEventsIfTheDatabaseIsEmpty()
    {
        var client = Container!.GetClient();
        var didReadEvents = false;

        var options = new ObserveEventsOptions(Recursive: true);
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await foreach (var _ in client.ObserveEventsAsync("/", options, source.Token))
            {
                didReadEvents = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Ignored, we cancel on our own
        }

        Assert.False(didReadEvents);
    }

    [Fact]
    public async Task ObserveAllEventsFromTheGivenSubject()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent, secondEvent], token: TestContext.Current.CancellationToken);

        var observedEvents = new List<Event>();
        var options = new ObserveEventsOptions(Recursive: true);
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await foreach (var eventResult in client.ObserveEventsAsync("/", options, source.Token))
            {
                observedEvents.Add(eventResult);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignored, we cancel on our own
        }

        Assert.Equal(2, observedEvents.Count);
    }

    [Fact]
    public async Task ObservesWithLowerBound()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent, secondEvent], token: TestContext.Current.CancellationToken);

        var observedEvents = new List<Event>();
        var options = new ObserveEventsOptions(Recursive: true, LowerBound: new Bound("1", BoundType.Inclusive));
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await foreach (var eventResult in client.ObserveEventsAsync("/", options, source.Token))
            {
                observedEvents.Add(eventResult);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignored, we cancel on our own
        }

        Assert.Single(observedEvents);
        Assert.Collection(observedEvents,
            foundEvent =>
            {
                Assert.Equal("1", foundEvent.Id);
                Assert.Equal(secondData, foundEvent.GetData<EventData>());
            }
        );
    }

    [Fact]
    public async Task ObservesFromLatestEvent()
    {
        var client = Container!.GetClient();

        var firstData = new EventData(23);
        var secondData = new EventData(42);

        var firstEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: firstData
        );
        var secondEvent = new EventCandidate(
            Source: "https://www.eventsourcingdb.io",
            Subject: "/test",
            Type: "io.eventsourcingdb.test",
            Data: secondData
        );

        await client.WriteEventsAsync([firstEvent, secondEvent], token: TestContext.Current.CancellationToken);

        var observedEvents = new List<Event>();
        var options = new ObserveEventsOptions(
            Recursive: true,
            FromLatestEvent: new ObserveFromLatestEvent(
                "/test",
                "io.eventsourcingdb.test",
                ObserveIfEventIsMissing.ReadEverything
            )
        );
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await foreach (var eventResult in client.ObserveEventsAsync("/", options, source.Token))
            {
                observedEvents.Add(eventResult);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignored, we cancel on our own
        }

        Assert.Single(observedEvents);
        Assert.Collection(observedEvents,
            foundEvent =>
            {
                Assert.Equal("1", foundEvent.Id);
                Assert.Equal(secondData, foundEvent.GetData<EventData>());
            }
        );
    }

    // This test exists to ensure that heartbeats are being logged as expected.
    // In a previous version we logged heartbeats too often.
    // Here we use a logger to be able to observe the heartbeat logs when running the test.
    [Fact]
    public async Task HeartbeatIsLoggedEverySecond()
    {
        var logger = GetClientLogger(testOutputHelper);
        var client = Container!.GetClient(logger: logger);

        var options = new ObserveEventsOptions(Recursive: true);

        var timeoutToReceiveTwoHeartbeats = TimeSpan.FromMilliseconds(2000);
        using var source = new CancellationTokenSource(timeoutToReceiveTwoHeartbeats);

        try
        {
            await foreach (var _ in client.ObserveEventsAsync("/", options, source.Token))
            {
                // We don't care about the events, just the heartbeats
            }
        }
        catch (OperationCanceledException)
        {
            // Ignored, we cancel on our own
        }
    }

    private record struct EventData(int Value);
}
