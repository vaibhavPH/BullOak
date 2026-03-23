using System.Text.Json;
using EventStore.Client;
using FluentAssertions;

namespace BullOak.Test.EventStore.Integration;

/// <summary>
/// Tests demonstrating EventStoreDB projections.
///
/// Projections are server-side JavaScript functions that run inside EventStoreDB.
/// They process events from one or more streams and can:
///   - Create new "projected" streams from existing ones
///   - Aggregate data across streams (e.g., count all orders)
///   - Partition events into category streams (e.g., all events for a customer)
///
/// EventStoreDB has built-in system projections (like $by_category, $by_event_type)
/// and supports user-defined custom projections.
/// </summary>
[Collection("EventStore")]
public class ProjectionTests
{
    private readonly EventStoreFixture _fixture;

    public ProjectionTests(EventStoreFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The $by_category system projection groups streams by their prefix.
    /// A stream named "order-123" belongs to category "order".
    /// This creates a category stream $ce-order that links to all events in order-* streams.
    /// </summary>
    [Fact]
    public async Task ByCategoryProjection_GroupsStreamsByPrefix()
    {
        // Arrange: write events to two different order streams
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var stream1 = $"order-{orderId1}";
        var stream2 = $"order-{orderId2}";

        await _fixture.Client.AppendToStreamAsync(
            stream1,
            StreamState.NoStream,
            new[] { ToEventData(new OrderCreated(orderId1, "CatAlice", DateTime.UtcNow)) });

        await _fixture.Client.AppendToStreamAsync(
            stream2,
            StreamState.NoStream,
            new[] { ToEventData(new OrderCreated(orderId2, "CatBob", DateTime.UtcNow)) });

        // The $by_category projection creates a category stream "$ce-order"
        // that contains links to all events in streams starting with "order-".
        // We need to wait for the projection to process — eventual consistency.
        await Task.Delay(3000);

        // Act: read from the category stream
        var categoryStream = "$ce-order";
        var events = new List<ResolvedEvent>();

        try
        {
            var result = _fixture.Client.ReadStreamAsync(
                Direction.Forwards,
                categoryStream,
                StreamPosition.Start,
                resolveLinkTos: true); // important: resolve links to get actual event data

            events = await result.ToListAsync();
        }
        catch (StreamNotFoundException)
        {
            // Projection may not have processed yet — this is acceptable in tests
        }

        // Assert: category stream should contain events from both order streams
        // Note: in a real scenario we'd assert more precisely, but the projection
        // may include events from other tests too. We verify the mechanism works.
        events.Should().NotBeEmpty("the $by_category projection should create $ce-order");

        var customerNames = events
            .Where(e => e.Event.EventType == nameof(OrderCreated))
            .Select(e => JsonSerializer.Deserialize<OrderCreated>(e.Event.Data.Span)!)
            .Select(o => o.CustomerName)
            .ToList();

        customerNames.Should().Contain("CatAlice");
        customerNames.Should().Contain("CatBob");
    }

    /// <summary>
    /// The $by_event_type system projection creates streams grouped by event type.
    /// All OrderCreated events end up in $et-OrderCreated, all ItemAddedToOrder in $et-ItemAddedToOrder, etc.
    /// </summary>
    [Fact]
    public async Task ByEventTypeProjection_GroupsEventsByType()
    {
        // Arrange: write different event types
        var orderId = Guid.NewGuid();
        var streamName = $"order-{orderId}";

        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            new[]
            {
                ToEventData(new OrderCreated(orderId, "TypeTest", DateTime.UtcNow)),
                ToEventData(new OrderCompleted(orderId, DateTime.UtcNow)),
            });

        // Wait for projection to process
        await Task.Delay(3000);

        // Act: read from the event-type stream
        var eventTypeStream = $"$et-{nameof(OrderCompleted)}";
        var events = new List<ResolvedEvent>();

        try
        {
            var result = _fixture.Client.ReadStreamAsync(
                Direction.Forwards,
                eventTypeStream,
                StreamPosition.Start,
                resolveLinkTos: true);

            events = await result.ToListAsync();
        }
        catch (StreamNotFoundException)
        {
            // Projection may not have caught up yet
        }

        // Assert
        events.Should().NotBeEmpty("the $by_event_type projection should create $et-OrderCompleted");
        events.Should().OnlyContain(e => e.Event.EventType == nameof(OrderCompleted));
    }

    /// <summary>
    /// Demonstrates reading from the $all stream with server-side filtering.
    /// This achieves a similar result to projections — gathering events across streams —
    /// but uses client-side processing instead of server-side JavaScript.
    /// This is often simpler and more reliable than custom projections for read models.
    /// </summary>
    [Fact]
    public async Task ReadAllWithFilter_CountsOrdersPerCustomer()
    {
        // Arrange: write OrderCreated events to different streams
        var stream1 = $"order-{Guid.NewGuid()}";
        var stream2 = $"order-{Guid.NewGuid()}";
        var stream3 = $"order-{Guid.NewGuid()}";

        // Use unique customer names to avoid interference from other tests
        var testId = Guid.NewGuid().ToString("N")[..6];
        var alice = $"ReadAllAlice-{testId}";
        var bob = $"ReadAllBob-{testId}";

        await _fixture.Client.AppendToStreamAsync(stream1, StreamState.NoStream,
            new[] { ToEventData(new OrderCreated(Guid.NewGuid(), alice, DateTime.UtcNow)) });
        await _fixture.Client.AppendToStreamAsync(stream2, StreamState.NoStream,
            new[] { ToEventData(new OrderCreated(Guid.NewGuid(), bob, DateTime.UtcNow)) });
        await _fixture.Client.AppendToStreamAsync(stream3, StreamState.NoStream,
            new[] { ToEventData(new OrderCreated(Guid.NewGuid(), alice, DateTime.UtcNow)) });

        // Act: read from $all, filtering to only OrderCreated events
        var filter = EventTypeFilter.Prefix(nameof(OrderCreated));
        var allEvents = _fixture.Client.ReadAllAsync(
            Direction.Forwards,
            Position.Start,
            filter);

        // Build a customer → order count dictionary (client-side projection)
        var customerOrderCounts = new Dictionary<string, int>();

        await foreach (var resolved in allEvents)
        {
            var order = JsonSerializer.Deserialize<OrderCreated>(resolved.Event.Data.Span);
            if (order == null) continue;

            if (!customerOrderCounts.ContainsKey(order.CustomerName))
                customerOrderCounts[order.CustomerName] = 0;
            customerOrderCounts[order.CustomerName]++;
        }

        // Assert: our test customers should have the expected counts
        customerOrderCounts.Should().ContainKey(alice);
        customerOrderCounts[alice].Should().Be(2);
        customerOrderCounts.Should().ContainKey(bob);
        customerOrderCounts[bob].Should().Be(1);
    }

    private static EventData ToEventData<T>(T @event) where T : notnull
    {
        return new EventData(
            Uuid.NewUuid(),
            typeof(T).Name,
            JsonSerializer.SerializeToUtf8Bytes(@event));
    }
}
