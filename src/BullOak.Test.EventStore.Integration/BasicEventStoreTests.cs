using System.Text.Json;
using EventStore.Client;
using FluentAssertions;

namespace BullOak.Test.EventStore.Integration;

/// <summary>
/// Basic integration tests demonstrating fundamental EventStoreDB operations.
/// Each test uses a unique stream name to avoid interference between tests.
///
/// These tests teach:
///   1. How to write events to a stream
///   2. How to read events back from a stream
///   3. How event ordering works (append-only log)
///   4. How optimistic concurrency works (expected revision)
///   5. How to read from the $all stream
/// </summary>
[Collection("EventStore")]
public class BasicEventStoreTests
{
    private readonly EventStoreFixture _fixture;

    public BasicEventStoreTests(EventStoreFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The simplest possible test: write one event and read it back.
    /// This verifies the container is running and the client can connect.
    /// </summary>
    [Fact]
    public async Task WriteAndReadSingleEvent()
    {
        // Arrange: create a unique stream name so tests don't interfere
        var streamName = $"order-{Guid.NewGuid()}";
        var orderCreated = new OrderCreated(Guid.NewGuid(), "Alice", DateTime.UtcNow);

        // Act: serialize the event and write it to EventStore
        var eventData = new EventData(
            Uuid.NewUuid(),
            nameof(OrderCreated),   // event type stored as metadata
            JsonSerializer.SerializeToUtf8Bytes(orderCreated));

        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,   // expect the stream doesn't exist yet
            new[] { eventData });

        // Assert: read the stream and verify the event
        var events = _fixture.Client.ReadStreamAsync(
            Direction.Forwards,
            streamName,
            StreamPosition.Start);

        var resolvedEvents = await events.ToListAsync();
        resolvedEvents.Should().HaveCount(1);

        var stored = JsonSerializer.Deserialize<OrderCreated>(
            resolvedEvents[0].Event.Data.Span);

        stored!.CustomerName.Should().Be("Alice");
        stored.OrderId.Should().Be(orderCreated.OrderId);
    }

    /// <summary>
    /// Demonstrates appending multiple events to a stream and reading them in order.
    /// Events in EventStoreDB are stored as an append-only log — order is guaranteed.
    /// </summary>
    [Fact]
    public async Task WriteMultipleEventsAndReadInOrder()
    {
        // Arrange
        var streamName = $"order-{Guid.NewGuid()}";
        var orderId = Guid.NewGuid();

        var events = new[]
        {
            ToEventData(new OrderCreated(orderId, "Bob", DateTime.UtcNow)),
            ToEventData(new ItemAddedToOrder(orderId, "Widget", 2, 9.99m)),
            ToEventData(new ItemAddedToOrder(orderId, "Gadget", 1, 24.99m)),
            ToEventData(new OrderCompleted(orderId, DateTime.UtcNow)),
        };

        // Act: write all 4 events in a single batch
        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            events);

        // Assert: read them back and verify order + types
        var readResult = _fixture.Client.ReadStreamAsync(
            Direction.Forwards,
            streamName,
            StreamPosition.Start);

        var resolvedEvents = await readResult.ToListAsync();
        resolvedEvents.Should().HaveCount(4);

        // Events come back in the same order they were written
        resolvedEvents[0].Event.EventType.Should().Be(nameof(OrderCreated));
        resolvedEvents[1].Event.EventType.Should().Be(nameof(ItemAddedToOrder));
        resolvedEvents[2].Event.EventType.Should().Be(nameof(ItemAddedToOrder));
        resolvedEvents[3].Event.EventType.Should().Be(nameof(OrderCompleted));

        // Verify the second item event has the right data
        var item = JsonSerializer.Deserialize<ItemAddedToOrder>(
            resolvedEvents[2].Event.Data.Span);
        item!.ProductName.Should().Be("Gadget");
        item.Quantity.Should().Be(1);
    }

    /// <summary>
    /// Demonstrates optimistic concurrency control.
    /// If two writers try to append to the same stream at the same position,
    /// the second write should fail with a WrongExpectedVersionException.
    /// This is how EventStore prevents lost updates without locks.
    /// </summary>
    [Fact]
    public async Task ConcurrencyConflictThrowsWrongExpectedVersion()
    {
        // Arrange: create a stream with one event
        var streamName = $"order-{Guid.NewGuid()}";
        var orderId = Guid.NewGuid();

        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            new[] { ToEventData(new OrderCreated(orderId, "Charlie", DateTime.UtcNow)) });

        // Act & Assert: try to append as if stream doesn't exist (wrong expected state)
        // This simulates a second writer who didn't see the first write.
        var conflictingWrite = () => _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,   // stream already exists!
            new[] { ToEventData(new ItemAddedToOrder(orderId, "Conflict", 1, 1.00m)) });

        await conflictingWrite.Should().ThrowAsync<WrongExpectedVersionException>();
    }

    /// <summary>
    /// Demonstrates appending to a stream using an explicit expected revision.
    /// After writing N events, the stream revision is N-1 (zero-based).
    /// You must pass the correct revision to append more events.
    /// </summary>
    [Fact]
    public async Task AppendWithExpectedRevision()
    {
        // Arrange
        var streamName = $"order-{Guid.NewGuid()}";
        var orderId = Guid.NewGuid();

        // Write the first event — stream starts at revision 0
        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            new[] { ToEventData(new OrderCreated(orderId, "Diana", DateTime.UtcNow)) });

        // Act: append using the correct expected revision (0 = one event exists)
        var result = await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamRevision.FromInt64(0), // we know there's 1 event at revision 0
            new[] { ToEventData(new ItemAddedToOrder(orderId, "Book", 3, 12.50m)) });

        // Assert: stream now has 2 events, next expected revision is 1
        result.NextExpectedStreamRevision.Should().Be(StreamRevision.FromInt64(1));

        var events = await _fixture.Client.ReadStreamAsync(
            Direction.Forwards, streamName, StreamPosition.Start)
            .ToListAsync();

        events.Should().HaveCount(2);
    }

    /// <summary>
    /// Demonstrates reading events backwards from the end of the stream.
    /// Useful for getting the latest state or recent events.
    /// </summary>
    [Fact]
    public async Task ReadStreamBackwards()
    {
        // Arrange
        var streamName = $"order-{Guid.NewGuid()}";
        var orderId = Guid.NewGuid();

        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            new[]
            {
                ToEventData(new OrderCreated(orderId, "Eve", DateTime.UtcNow)),
                ToEventData(new ItemAddedToOrder(orderId, "Pen", 5, 1.99m)),
                ToEventData(new OrderCompleted(orderId, DateTime.UtcNow)),
            });

        // Act: read backwards from end, only take 1 (the most recent)
        var events = await _fixture.Client.ReadStreamAsync(
            Direction.Backwards,
            streamName,
            StreamPosition.End,
            maxCount: 1)
            .ToListAsync();

        // Assert: we get the last event (OrderCompleted)
        events.Should().HaveCount(1);
        events[0].Event.EventType.Should().Be(nameof(OrderCompleted));
    }

    /// <summary>
    /// Demonstrates reading a non-existent stream.
    /// EventStoreDB throws StreamNotFoundException for streams that have never been written to.
    /// </summary>
    [Fact]
    public async Task ReadNonExistentStreamThrows()
    {
        var streamName = $"does-not-exist-{Guid.NewGuid()}";

        var readAction = async () =>
        {
            var result = _fixture.Client.ReadStreamAsync(
                Direction.Forwards,
                streamName,
                StreamPosition.Start);
            await result.ToListAsync();
        };

        await readAction.Should().ThrowAsync<StreamNotFoundException>();
    }

    /// <summary>
    /// Helper: serialize any event object into an EventData that EventStoreDB understands.
    /// </summary>
    private static EventData ToEventData<T>(T @event) where T : notnull
    {
        return new EventData(
            Uuid.NewUuid(),
            typeof(T).Name,    // store the CLR type name as the event type
            JsonSerializer.SerializeToUtf8Bytes(@event));
    }
}
