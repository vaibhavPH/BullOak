using System.Text.Json;
using EventStore.Client;
using FluentAssertions;

namespace BullOak.Test.EventStore.Integration;

/// <summary>
/// Tests demonstrating EventStoreDB stream subscriptions.
///
/// Subscriptions are a way to get notified when new events are written.
/// Unlike reading (which is a one-time pull), subscriptions "push" events to you
/// as they arrive — essential for building read models, projections, and reactive systems.
///
/// Two types:
///   - Stream subscription: watch a specific stream (e.g., "order-123")
///   - $all subscription: watch ALL events across ALL streams (with optional filtering)
/// </summary>
[Collection("EventStore")]
public class SubscriptionTests
{
    private readonly EventStoreFixture _fixture;

    public SubscriptionTests(EventStoreFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Subscribe to a specific stream and receive events as they are written.
    /// The subscription starts from the beginning of the stream and catches up.
    /// </summary>
    [Fact]
    public async Task SubscribeToStream_ReceivesNewEvents()
    {
        // Arrange: write some events first
        var streamName = $"order-{Guid.NewGuid()}";
        var orderId = Guid.NewGuid();

        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            new[]
            {
                ToEventData(new OrderCreated(orderId, "Sub-Alice", DateTime.UtcNow)),
                ToEventData(new ItemAddedToOrder(orderId, "Laptop", 1, 999.99m)),
            });

        // Act: subscribe from the start and collect events
        var receivedEvents = new List<ResolvedEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = _fixture.Client.SubscribeToStream(
            streamName,
            FromStream.Start,
            cancellationToken: cts.Token);

        // Read events from the subscription (it's an IAsyncEnumerable)
        await foreach (var @event in subscription)
        {
            receivedEvents.Add(@event);

            // We know there are 2 events, so stop after receiving both
            if (receivedEvents.Count >= 2)
                break;
        }

        // Assert
        receivedEvents.Should().HaveCount(2);
        receivedEvents[0].Event.EventType.Should().Be(nameof(OrderCreated));
        receivedEvents[1].Event.EventType.Should().Be(nameof(ItemAddedToOrder));
    }

    /// <summary>
    /// Subscribe from the end of a stream, then write new events.
    /// This simulates a "live" subscription that only gets future events.
    /// </summary>
    [Fact]
    public async Task SubscribeToStream_FromEnd_ReceivesOnlyNewEvents()
    {
        // Arrange: create stream with an initial event
        var streamName = $"order-{Guid.NewGuid()}";
        var orderId = Guid.NewGuid();

        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            new[] { ToEventData(new OrderCreated(orderId, "Sub-Bob", DateTime.UtcNow)) });

        // Act: subscribe from the END (won't see the OrderCreated above)
        var receivedEvents = new List<ResolvedEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var subscription = _fixture.Client.SubscribeToStream(
            streamName,
            FromStream.End,
            cancellationToken: cts.Token);

        // Start consuming in the background
        var readTask = Task.Run(async () =>
        {
            await foreach (var @event in subscription)
            {
                receivedEvents.Add(@event);
                if (receivedEvents.Count >= 1)
                    break;
            }
        }, cts.Token);

        // Give the subscription a moment to establish
        await Task.Delay(500);

        // Write a NEW event after the subscription is active
        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamRevision.FromInt64(0),
            new[] { ToEventData(new ItemAddedToOrder(orderId, "Mouse", 2, 25.00m)) });

        await readTask;

        // Assert: should only see the new event, not the initial OrderCreated
        receivedEvents.Should().HaveCount(1);
        receivedEvents[0].Event.EventType.Should().Be(nameof(ItemAddedToOrder));
    }

    /// <summary>
    /// Subscribe to $all — catches events from every stream in the database.
    /// Uses an event type filter to only receive specific event types.
    /// This is a powerful pattern for building cross-stream projections.
    /// </summary>
    [Fact]
    public async Task SubscribeToAll_WithTypeFilter_ReceivesFilteredEvents()
    {
        // Arrange: write events to different streams
        var stream1 = $"order-{Guid.NewGuid()}";
        var stream2 = $"order-{Guid.NewGuid()}";
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();

        await _fixture.Client.AppendToStreamAsync(
            stream1,
            StreamState.NoStream,
            new[]
            {
                ToEventData(new OrderCreated(orderId1, "FilterAlice", DateTime.UtcNow)),
                ToEventData(new OrderCompleted(orderId1, DateTime.UtcNow)),
            });

        await _fixture.Client.AppendToStreamAsync(
            stream2,
            StreamState.NoStream,
            new[]
            {
                ToEventData(new OrderCreated(orderId2, "FilterBob", DateTime.UtcNow)),
                ToEventData(new ItemAddedToOrder(orderId2, "Keyboard", 1, 49.99m)),
            });

        // Act: subscribe to $all with a filter for OrderCompleted events only
        var receivedEvents = new List<ResolvedEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // EventTypeFilter.Prefix filters events whose type starts with the given prefix
        var filterOptions = new SubscriptionFilterOptions(
            EventTypeFilter.Prefix(nameof(OrderCompleted)));

        var subscription = _fixture.Client.SubscribeToAll(
            FromAll.Start,
            filterOptions: filterOptions,
            cancellationToken: cts.Token);

        await foreach (var @event in subscription)
        {
            receivedEvents.Add(@event);

            // We wrote 1 OrderCompleted event, stop after we see it
            if (receivedEvents.Count >= 1)
                break;
        }

        // Assert: only OrderCompleted events come through
        receivedEvents.Should().NotBeEmpty();
        receivedEvents.Should().OnlyContain(e => e.Event.EventType == nameof(OrderCompleted));
    }

    private static EventData ToEventData<T>(T @event) where T : notnull
    {
        return new EventData(
            Uuid.NewUuid(),
            typeof(T).Name,
            JsonSerializer.SerializeToUtf8Bytes(@event));
    }
}
