# BullOak.Test.EventStore.Integration

Integration test suite that runs a **real EventStoreDB instance** inside Docker using [TestContainers](https://dotnet.testcontainers.org/) and verifies BullOak's event sourcing capabilities against it.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Architecture Overview](#architecture-overview)
- [Test Classes](#test-classes)
  - [BasicEventStoreTests](#basiceventstoretest---6-tests)
  - [SubscriptionTests](#subscriptiontests---3-tests)
  - [ProjectionTests](#projectiontests---3-tests)
  - [BullOakIntegrationTests](#bulloakintegrationtests---3-tests)
- [Project Structure](#project-structure)
- [How the Docker Fixture Works](#how-the-docker-fixture-works)
- [Key Concepts Explained](#key-concepts-explained)
  - [Event Sourcing Basics](#event-sourcing-basics)
  - [EventStoreDB Streams](#eventstoredb-streams)
  - [Optimistic Concurrency](#optimistic-concurrency)
  - [Subscriptions vs Reading](#subscriptions-vs-reading)
  - [System Projections](#system-projections)
  - [BullOak State Rehydration](#bulloak-state-rehydration)
- [Bridging BullOak with EventStoreDB](#bridging-bulloak-with-eventstoredb)
- [Dependencies](#dependencies)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

- **.NET 8.0 SDK** or later
- **Docker Desktop** running (required for TestContainers)
- The EventStoreDB Docker image will be pulled automatically on first run

## Quick Start

```bash
# From the repository root
cd src

# Run all integration tests
dotnet test BullOak.Test.EventStore.Integration

# Run a specific test class
dotnet test BullOak.Test.EventStore.Integration --filter "BasicEventStoreTests"

# Run with detailed output
dotnet test BullOak.Test.EventStore.Integration --logger "console;verbosity=detailed"
```

> **First run note:** The initial run will pull the `eventstore/eventstore:latest` Docker image (~500MB), so it may take a few minutes. Subsequent runs reuse the cached image.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    xUnit Test Runner                     │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ┌─────────────────┐  ┌──────────────────────────────┐  │
│  │ EventStoreFixture│  │  [Collection("EventStore")]  │  │
│  │ (IAsyncLifetime) │◄─┤  Shared across all tests     │  │
│  │                  │  └──────────────────────────────┘  │
│  │  Starts Docker   │                                    │
│  │  container with  │  ┌──────────────────────────────┐  │
│  │  EventStoreDB    │  │  Test Classes:                │  │
│  │                  │  │  • BasicEventStoreTests       │  │
│  │  Provides:       │  │  • SubscriptionTests          │  │
│  │  • EventStore    │──│  • ProjectionTests            │  │
│  │    Client        │  │  • BullOakIntegrationTests    │  │
│  │  • Connection    │  └──────────────────────────────┘  │
│  │    String        │                                    │
│  └────────┬─────────┘                                    │
│           │                                              │
├───────────┼──────────────────────────────────────────────┤
│           ▼          Docker                              │
│  ┌─────────────────┐                                     │
│  │  EventStoreDB   │  Port 2113 (gRPC + HTTP)           │
│  │  (in-memory,    │  Insecure mode (no TLS)            │
│  │   single node)  │  All projections enabled           │
│  └─────────────────┘                                     │
└─────────────────────────────────────────────────────────┘
```

---

## Test Classes

### BasicEventStoreTests — 6 tests

**File:** `BasicEventStoreTests.cs`

Covers the fundamental operations of EventStoreDB. Start here if you're new to event sourcing.

| Test | What You'll Learn |
|------|-------------------|
| `WriteAndReadSingleEvent` | Serialize an event, write it to a stream, read it back and deserialize. The "hello world" of event sourcing. |
| `WriteMultipleEventsAndReadInOrder` | Batch-write 4 events and verify they come back in the exact same order. Demonstrates EventStoreDB's **append-only log** guarantee. |
| `ConcurrencyConflictThrowsWrongExpectedVersion` | Two writers conflict — the second gets `WrongExpectedVersionException`. This is **optimistic concurrency** in action. |
| `AppendWithExpectedRevision` | Safely append to an existing stream by specifying the exact revision you expect. Shows how to do **sequential writes** without conflicts. |
| `ReadStreamBackwards` | Read from the end of a stream — useful for "get latest state" queries without replaying the entire history. |
| `ReadNonExistentStreamThrows` | Attempting to read a stream that was never written to throws `StreamNotFoundException`. |

### SubscriptionTests — 3 tests

**File:** `SubscriptionTests.cs`

Demonstrates real-time event streaming via subscriptions.

| Test | What You'll Learn |
|------|-------------------|
| `SubscribeToStream_ReceivesNewEvents` | Subscribe from the **start** of a stream and catch up on existing events. The subscription is an `IAsyncEnumerable<ResolvedEvent>`. |
| `SubscribeToStream_FromEnd_ReceivesOnlyNewEvents` | Subscribe from the **end** — only future events are delivered. Simulates a "live" listener that ignores history. |
| `SubscribeToAll_WithTypeFilter_ReceivesFilteredEvents` | Subscribe to `$all` (every stream) but filter by event type prefix. This is how you build **cross-stream read models**. |

### ProjectionTests — 3 tests

**File:** `ProjectionTests.cs`

Shows how EventStoreDB organizes events across streams via projections.

| Test | What You'll Learn |
|------|-------------------|
| `ByCategoryProjection_GroupsStreamsByPrefix` | The `$by_category` system projection creates `$ce-order` from all `order-*` streams. Read one stream to get **all orders**. |
| `ByEventTypeProjection_GroupsEventsByType` | The `$by_event_type` projection creates `$et-OrderCompleted` containing **all OrderCompleted events** regardless of which stream they came from. |
| `ReadAllWithFilter_CountsOrdersPerCustomer` | Client-side projection: read from `$all` with an `EventTypeFilter`, then aggregate in C#. This is the **recommended pattern** for building read models — simpler and more predictable than server-side JS projections. |

### BullOakIntegrationTests — 3 tests

**File:** `BullOakIntegrationTests.cs`

The core integration: BullOak's event sourcing framework meets a real EventStoreDB.

| Test | What You'll Learn |
|------|-------------------|
| `RehydrateStateFromEventStore` | Read events from EventStoreDB, convert to `StoredEvent[]`, and use BullOak's `StateRehydrator` to reconstruct an `IAccountState`. This is the **read path**. |
| `RehydrateStateAsyncFromEventStore` | Same as above but using `IAsyncEnumerable<StoredEvent>` — events are streamed lazily. This is the **production-ready pattern** for large streams. |
| `RoundTrip_WriteWithBullOak_ReadFromEventStore` | Full cycle: create events via BullOak's session → persist to EventStoreDB → read back → rehydrate via BullOak. Proves the **bridge** between the two systems works end-to-end. |

---

## Project Structure

```
BullOak.Test.EventStore.Integration/
│
├── EventStoreFixture.cs        # Docker container lifecycle (start/stop EventStoreDB)
├── EventStoreCollection.cs     # xUnit collection definition (shares fixture across tests)
│
├── Events.cs                   # Order domain events: OrderCreated, ItemAddedToOrder, OrderCompleted
│
├── BasicEventStoreTests.cs     # Fundamentals: write, read, concurrency, backwards reading
├── SubscriptionTests.cs        # Real-time: stream subscriptions, $all with filtering
├── ProjectionTests.cs          # Organization: system projections, client-side aggregation
├── BullOakIntegrationTests.cs  # Bridge: BullOak rehydration + EventStoreDB storage
│                                 Also defines: AccountOpened, MoneyDeposited, MoneyWithdrawn events,
│                                               IAccountState interface, AccountStateApplier
│
├── BullOak.Test.EventStore.Integration.csproj
└── Usings.cs
```

---

## How the Docker Fixture Works

The `EventStoreFixture` class manages the entire EventStoreDB lifecycle:

```
Test Discovery
     │
     ▼
xUnit sees [Collection("EventStore")] on test classes
     │
     ▼
Creates ONE EventStoreFixture instance (shared across all test classes)
     │
     ▼
Calls InitializeAsync():
  1. Starts Docker container (eventstore/eventstore:latest)
  2. Container config:
     - EVENTSTORE_INSECURE=true          → No TLS (faster startup)
     - EVENTSTORE_CLUSTER_SIZE=1         → Single node
     - EVENTSTORE_RUN_PROJECTIONS=All    → Enable system projections
     - EVENTSTORE_MEM_DB=true            → In-memory (no disk I/O)
  3. Waits for /health/live endpoint to return 204 No Content
  4. Creates EventStoreClient connected to the random mapped port
     │
     ▼
All tests run using the shared Client
     │
     ▼
Calls DisposeAsync():
  1. Disposes EventStoreClient
  2. Stops and removes Docker container
```

**Why each test uses a unique stream name:** Since all tests share one EventStoreDB instance, each test generates a unique stream name like `order-{Guid.NewGuid()}`. This provides **complete test isolation** without the overhead of spinning up separate containers.

---

## Key Concepts Explained

### Event Sourcing Basics

Instead of storing the **current state** of an entity (like a row in a database), you store the **sequence of events** that led to that state:

```
Traditional:  Account { balance: 320 }

Event-sourced:
  1. AccountOpened    { initialDeposit: 100 }
  2. MoneyDeposited   { amount: 50 }
  3. MoneyWithdrawn   { amount: 30 }
  4. MoneyDeposited   { amount: 200 }

  → Replay: 100 + 50 - 30 + 200 = 320
```

**Benefits:** Full audit trail, time-travel debugging, event-driven integrations, no data loss.

### EventStoreDB Streams

A **stream** is a named, ordered sequence of events. Think of it as a topic or a partition key:

- `order-abc123` — events for order abc123
- `account-jane` — events for Jane's account

Streams are **append-only** — you can only add events to the end, never modify or delete individual events.

### Optimistic Concurrency

EventStoreDB uses **optimistic concurrency** to prevent lost updates:

```csharp
// Writer A reads stream at revision 5, prepares an event
// Writer B reads stream at revision 5, prepares an event
// Writer A writes with expectedRevision: 5 → succeeds, stream is now at revision 6
// Writer B writes with expectedRevision: 5 → FAILS with WrongExpectedVersionException
```

This is the same pattern BullOak uses in its `InMemoryEventStoreSession` — it checks `stream.Count != initialVersion` before saving.

### Subscriptions vs Reading

| | Reading | Subscription |
|---|---------|-------------|
| **Model** | Pull (one-time) | Push (continuous) |
| **Use case** | Load state, replay history | Build read models, react to events |
| **API** | `ReadStreamAsync` | `SubscribeToStream` / `SubscribeToAll` |
| **Returns** | Finite `IAsyncEnumerable` | Infinite `IAsyncEnumerable` (blocks until cancelled) |

### System Projections

EventStoreDB includes built-in projections that automatically organize events:

| Projection | Creates | Example |
|-----------|---------|---------|
| `$by_category` | `$ce-{category}` streams | `order-123`, `order-456` → `$ce-order` |
| `$by_event_type` | `$et-{eventType}` streams | All `OrderCreated` events → `$et-OrderCreated` |
| `$streams` | `$streams` stream | Links to every stream in the database |

These are **eventually consistent** — there's a small delay between writing an event and it appearing in the projected stream (the tests use `Task.Delay(3000)` to account for this).

### BullOak State Rehydration

BullOak reconstructs state by **folding** events through registered appliers:

```
state₀ = new IAccountState()                        // empty state
state₁ = Apply(state₀, AccountOpened{100})           // balance: 100
state₂ = Apply(state₁, MoneyDeposited{50})           // balance: 150
state₃ = Apply(state₂, MoneyWithdrawn{30})           // balance: 120
```

The `IAccountState` interface is defined in the test project, and BullOak's `EmittedTypeFactory` dynamically generates a concrete implementation class at runtime using `System.Reflection.Emit`. The `AccountStateApplier` class implements `IApplyEvent<IAccountState, TEvent>` for each event type and is discovered automatically via assembly scanning.

---

## Bridging BullOak with EventStoreDB

The integration tests demonstrate the key bridge between the two systems. Here's the pattern:

### Reading from EventStoreDB → BullOak Rehydration

```csharp
// 1. Read events from EventStoreDB
var readResult = client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start);

// 2. Convert to BullOak's StoredEvent format
var storedEvents = new List<StoredEvent>();
var index = 0;
await foreach (var resolved in readResult)
{
    var eventType = resolved.Event.EventType;           // e.g., "AccountOpened"
    var eventObj = DeserializeEvent(eventType, data);    // JSON → CLR object
    storedEvents.Add(new StoredEvent(eventObj.GetType(), eventObj, index++));
}

// 3. Use BullOak's rehydrator
var result = configuration.StateRehydrator.RehydrateFrom<IAccountState>(storedEvents.ToArray());
var state = result.State;  // fully reconstructed state
```

### Writing BullOak Events → EventStoreDB

```csharp
// 1. Get events from BullOak session/repository
var bullOakEvents = repo[streamName];  // List<(StoredEvent, DateTime)>

// 2. Convert to EventStoreDB's EventData format
var eventDatas = bullOakEvents.Select(e => new EventData(
    Uuid.NewUuid(),
    e.Item1.EventType.Name,                                    // CLR type name
    JsonSerializer.SerializeToUtf8Bytes(e.Item1.Event, e.Item1.EventType)
)).ToArray();

// 3. Write to EventStoreDB
await client.AppendToStreamAsync(streamName, StreamState.NoStream, eventDatas);
```

This bridge code is what a production "BullOak.EventStore" adapter package would encapsulate.

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Testcontainers` | 4.3.0 | Manages Docker container lifecycle for tests |
| `EventStore.Client.Grpc.Streams` | 23.3.9 | gRPC client for EventStoreDB (read/write/subscribe) |
| `xunit` | 2.9.3 | Test framework |
| `FluentAssertions` | 6.12.2 | Readable assertion syntax (last free Apache 2.0 version) |
| `Microsoft.NET.Test.Sdk` | 17.12.0 | Test SDK |
| `BullOak.Repositories` | (project ref) | Core BullOak event sourcing library |

---

## Troubleshooting

### Docker is not running

```
Error: Docker is not running or not accessible
```

**Fix:** Start Docker Desktop and wait for it to be fully ready.

### Port conflicts

TestContainers maps EventStoreDB's port 2113 to a **random available host port**, so port conflicts should not occur. If they do, ensure no other EventStoreDB instances are running.

### Tests are slow on first run

The first run pulls the `eventstore/eventstore:latest` Docker image. Subsequent runs use the cached image and typically complete in ~30 seconds.

### Projection tests fail intermittently

System projections (`$by_category`, `$by_event_type`) are **eventually consistent**. The tests include a 3-second delay to account for this. If tests still fail:
- Ensure Docker has sufficient memory allocated (at least 2GB recommended)
- Increase the delay if running on slower hardware

### EventStoreDB container fails to start

Check Docker resource limits. EventStoreDB requires:
- At least 512MB RAM
- The container runs in memory-only mode (`EVENTSTORE_MEM_DB=true`), so no disk space is needed
