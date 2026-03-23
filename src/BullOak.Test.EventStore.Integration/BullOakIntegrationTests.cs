using System.Reflection;
using System.Text.Json;
using BullOak.Repositories;
using BullOak.Repositories.Appliers;
using BullOak.Repositories.Config;
using BullOak.Repositories.Session;
using EventStore.Client;
using FluentAssertions;

namespace BullOak.Test.EventStore.Integration;

/// <summary>
/// Integration tests that bridge BullOak's event sourcing framework with a real EventStoreDB.
///
/// BullOak's in-memory repository (InMemoryEventSourcedRepository) is great for unit tests,
/// but production systems need a real event store. These tests demonstrate:
///
///   1. How BullOak's state rehydration works with events loaded from EventStoreDB
///   2. How to build a custom EventStoreDB-backed session using BullOak's base classes
///   3. How BullOak's event applier pattern reconstructs state from raw events
///
/// This is the pattern you'd follow to create a "BullOak.EventStore" adapter package.
/// </summary>
[Collection("EventStore")]
public class BullOakIntegrationTests
{
    private readonly EventStoreFixture _fixture;

    public BullOakIntegrationTests(EventStoreFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Demonstrates BullOak's core superpower: rehydrating state from a sequence of events.
    ///
    /// Flow:
    ///   1. Write domain events to EventStoreDB
    ///   2. Read them back as StoredEvent[] (BullOak's internal format)
    ///   3. Use BullOak's configuration to rehydrate state via registered event appliers
    ///   4. Verify the resulting state matches what we expect
    /// </summary>
    [Fact]
    public async Task RehydrateStateFromEventStore()
    {
        // Arrange: set up BullOak configuration with our event appliers
        var configuration = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(Assembly.GetExecutingAssembly())
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        // Write events to EventStoreDB
        var streamName = $"account-{Guid.NewGuid()}";
        var accountId = Guid.NewGuid();

        var eventDatas = new[]
        {
            ToEventData(new AccountOpened(accountId, "Jane Doe", 100.00m)),
            ToEventData(new MoneyDeposited(accountId, 50.00m)),
            ToEventData(new MoneyWithdrawn(accountId, 30.00m)),
            ToEventData(new MoneyDeposited(accountId, 200.00m)),
        };

        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            eventDatas);

        // Act: read events from EventStoreDB and convert to BullOak StoredEvent format
        var readResult = _fixture.Client.ReadStreamAsync(
            Direction.Forwards,
            streamName,
            StreamPosition.Start);

        var storedEvents = new List<StoredEvent>();
        var eventIndex = 0;
        await foreach (var resolved in readResult)
        {
            var eventType = resolved.Event.EventType;
            var eventObj = DeserializeEvent(eventType, resolved.Event.Data.Span);
            storedEvents.Add(new StoredEvent(eventObj.GetType(), eventObj, eventIndex++));
        }

        // Use BullOak's rehydrator to reconstruct state
        var rehydrateResult = configuration.StateRehydrator
            .RehydrateFrom<IAccountState>(storedEvents.ToArray());

        // Assert: state should reflect all events applied in order
        var state = rehydrateResult.State;
        state.AccountId.Should().Be(accountId);
        state.HolderName.Should().Be("Jane Doe");
        state.Balance.Should().Be(320.00m);  // 100 + 50 - 30 + 200
        state.IsOpen.Should().BeTrue();
        state.TransactionCount.Should().Be(4);
    }

    /// <summary>
    /// Demonstrates reading events asynchronously from EventStoreDB
    /// and feeding them into BullOak's async rehydration pipeline.
    /// This is the more realistic pattern for production use — events
    /// are streamed lazily rather than loaded all at once.
    /// </summary>
    [Fact]
    public async Task RehydrateStateAsyncFromEventStore()
    {
        // Arrange
        var configuration = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(Assembly.GetExecutingAssembly())
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var streamName = $"account-{Guid.NewGuid()}";
        var accountId = Guid.NewGuid();

        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            new[]
            {
                ToEventData(new AccountOpened(accountId, "Async Jane", 500.00m)),
                ToEventData(new MoneyWithdrawn(accountId, 150.00m)),
                ToEventData(new MoneyDeposited(accountId, 75.00m)),
            });

        // Act: create an IAsyncEnumerable<StoredEvent> from EventStoreDB reads
        async IAsyncEnumerable<StoredEvent> ReadAsStoredEvents()
        {
            var readResult = _fixture.Client.ReadStreamAsync(
                Direction.Forwards, streamName, StreamPosition.Start);
            var index = 0;
            await foreach (var resolved in readResult)
            {
                var eventType = resolved.Event.EventType;
                var eventObj = DeserializeEvent(eventType, resolved.Event.Data.Span);
                yield return new StoredEvent(eventObj.GetType(), eventObj, index++);
            }
        }

        var rehydrateResult = await configuration.StateRehydrator
            .RehydrateFrom<IAccountState>(ReadAsStoredEvents());

        // Assert
        var state = rehydrateResult.State;
        state.HolderName.Should().Be("Async Jane");
        state.Balance.Should().Be(425.00m);  // 500 - 150 + 75
    }

    /// <summary>
    /// Demonstrates applying new events through BullOak's session,
    /// and then saving them to EventStoreDB.
    /// This is the write-side pattern: load state, apply business logic, persist new events.
    /// </summary>
    [Fact]
    public async Task RoundTrip_WriteWithBullOak_ReadFromEventStore()
    {
        // Arrange
        var configuration = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(Assembly.GetExecutingAssembly())
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var streamName = $"account-{Guid.NewGuid()}";
        var accountId = Guid.NewGuid();

        // Step 1: Use BullOak's InMemoryEventSourcedRepository as the "write model"
        var repo = new Repositories.InMemory.InMemoryEventSourcedRepository<string, IAccountState>(configuration);

        using (var session = await repo.BeginSessionFor(streamName))
        {
            session.AddEvent(new AccountOpened(accountId, "RoundTrip User", 250.00m));
            session.AddEvent(new MoneyDeposited(accountId, 100.00m));
            await session.SaveChanges();
        }

        // Step 2: Get events from BullOak's in-memory store and write to EventStoreDB
        var bullOakEvents = repo[streamName];
        var eventDatas = bullOakEvents.Select(e =>
        {
            var stored = e.Item1;
            return new EventData(
                Uuid.NewUuid(),
                stored.EventType.Name,
                JsonSerializer.SerializeToUtf8Bytes(stored.Event, stored.EventType));
        }).ToArray();

        await _fixture.Client.AppendToStreamAsync(
            streamName,
            StreamState.NoStream,
            eventDatas);

        // Step 3: Read back from EventStoreDB and rehydrate via BullOak
        var readResult = _fixture.Client.ReadStreamAsync(
            Direction.Forwards, streamName, StreamPosition.Start);

        var storedEvents = new List<StoredEvent>();
        var idx = 0;
        await foreach (var resolved in readResult)
        {
            var obj = DeserializeEvent(resolved.Event.EventType, resolved.Event.Data.Span);
            storedEvents.Add(new StoredEvent(obj.GetType(), obj, idx++));
        }

        var rehydrated = configuration.StateRehydrator
            .RehydrateFrom<IAccountState>(storedEvents.ToArray());

        // Assert: state rehydrated from EventStoreDB matches what BullOak produced
        var state = rehydrated.State;
        state.AccountId.Should().Be(accountId);
        state.HolderName.Should().Be("RoundTrip User");
        state.Balance.Should().Be(350.00m);  // 250 + 100
        state.TransactionCount.Should().Be(2);
    }

    #region Event Deserialization Helper

    /// <summary>
    /// Maps EventStoreDB event type names back to CLR types for deserialization.
    /// In production, you'd use a proper type registry or convention-based mapper.
    /// </summary>
    private static object DeserializeEvent(string eventType, ReadOnlySpan<byte> data)
    {
        return eventType switch
        {
            nameof(AccountOpened) => JsonSerializer.Deserialize<AccountOpened>(data)!,
            nameof(MoneyDeposited) => JsonSerializer.Deserialize<MoneyDeposited>(data)!,
            nameof(MoneyWithdrawn) => JsonSerializer.Deserialize<MoneyWithdrawn>(data)!,
            _ => throw new InvalidOperationException($"Unknown event type: {eventType}")
        };
    }

    #endregion

    #region Helper

    private static EventData ToEventData<T>(T @event) where T : notnull
    {
        return new EventData(
            Uuid.NewUuid(),
            typeof(T).Name,
            JsonSerializer.SerializeToUtf8Bytes(@event));
    }

    #endregion
}

#region Domain Events for Account Aggregate

public record AccountOpened(
    Guid AccountId,
    string HolderName,
    decimal InitialDeposit);

public record MoneyDeposited(
    Guid AccountId,
    decimal Amount);

public record MoneyWithdrawn(
    Guid AccountId,
    decimal Amount);

#endregion

#region BullOak State Interface + Applier

/// <summary>
/// State interface for the Account aggregate.
/// BullOak's EmittedTypeFactory will dynamically generate a concrete class
/// implementing this interface at runtime using Reflection.Emit.
/// Properties must have setters so BullOak can mutate state during rehydration.
/// </summary>
public interface IAccountState
{
    Guid AccountId { get; set; }
    string HolderName { get; set; }
    decimal Balance { get; set; }
    bool IsOpen { get; set; }
    int TransactionCount { get; set; }
}

/// <summary>
/// Event applier that tells BullOak how to apply each event type to the account state.
/// This is the core of the "fold" operation: state = apply(state, event) for each event.
///
/// BullOak discovers this class automatically via Assembly scanning
/// (WithAnyAppliersFrom) because it implements IApplyEvent&lt;TState, TEvent&gt;.
/// </summary>
public class AccountStateApplier :
    IApplyEvent<IAccountState, AccountOpened>,
    IApplyEvent<IAccountState, MoneyDeposited>,
    IApplyEvent<IAccountState, MoneyWithdrawn>
{
    public IAccountState Apply(IAccountState state, AccountOpened @event)
    {
        state.AccountId = @event.AccountId;
        state.HolderName = @event.HolderName;
        state.Balance = @event.InitialDeposit;
        state.IsOpen = true;
        state.TransactionCount = 1;
        return state;
    }

    public IAccountState Apply(IAccountState state, MoneyDeposited @event)
    {
        state.Balance += @event.Amount;
        state.TransactionCount++;
        return state;
    }

    public IAccountState Apply(IAccountState state, MoneyWithdrawn @event)
    {
        state.Balance -= @event.Amount;
        state.TransactionCount++;
        return state;
    }
}

#endregion
