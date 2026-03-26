# BullOak Docker & Integration Tests Guide

This guide provides comprehensive documentation for all Docker-based integration tests in the BullOak repository. These tests use [Testcontainers for .NET](https://dotnet.testcontainers.org/) to spin up real infrastructure (PostgreSQL, EventStoreDB) in Docker containers, ensuring tests run against production-equivalent environments rather than mocks.

> **Prerequisite:** Docker Desktop must be running on your machine before executing any integration test.

---

## Table of Contents

- [Why Docker-Based Integration Tests?](#why-docker-based-integration-tests)
- [Technology Stack](#technology-stack)
- [Test Infrastructure Pattern](#test-infrastructure-pattern)
  - [xUnit Collection Fixtures](#xunit-collection-fixtures)
  - [Container Lifecycle](#container-lifecycle)
- [PostgreSQL Integration Tests](#postgresql-integration-tests)
  - [Infrastructure Setup](#postgresql-infrastructure-setup)
  - [Domain Model (Bank Account)](#postgresql-domain-model)
  - [Test Catalog](#postgresql-test-catalog)
- [EventStoreDB Integration Tests](#eventstoredb-integration-tests)
  - [Infrastructure Setup](#eventstoredb-infrastructure-setup)
  - [Domain Models](#eventstoredb-domain-models)
  - [Basic EventStore Tests](#basic-eventstore-tests)
  - [BullOak Integration Tests](#bulloak-integration-tests)
  - [Projection Tests](#projection-tests)
  - [Subscription Tests](#subscription-tests)
- [CI/CD Pipeline](#cicd-pipeline)
- [Running Tests Locally](#running-tests-locally)
- [Troubleshooting](#troubleshooting)
- [PostgreSQL Read Model Projection Tests](#postgresql-read-model-projection-tests)
  - [Infrastructure Setup](#readmodel-infrastructure-setup)
  - [The Projector Pattern](#the-projector-pattern)
  - [Read Model Test Catalog](#read-model-test-catalog)
- [RabbitMQ + MassTransit Integration Tests](#rabbitmq--masstransit-integration-tests)
  - [Infrastructure Setup](#rabbitmq-infrastructure-setup)
  - [The Event Publisher Bridge](#the-event-publisher-bridge)
  - [RabbitMQ Test Catalog](#rabbitmq-test-catalog)

---

## Why Docker-Based Integration Tests?

Unit tests with mocks verify logic in isolation, but they cannot catch:

| Problem | Example |
|---------|---------|
| **Serialization mismatches** | An event serializes to JSON fine in-memory but fails when stored/retrieved from PostgreSQL JSONB |
| **SQL/query bugs** | A Dapper query works against your mental model but fails against real PostgreSQL (e.g., type coercion, NULL handling) |
| **Concurrency behavior** | Optimistic concurrency via database constraints behaves differently than in-memory locks |
| **Eventual consistency timing** | EventStoreDB projections take time to process — mocks can't simulate this |
| **Connection lifecycle issues** | Connection pooling, disposal, and reconnection bugs only surface with real databases |
| **Schema migration correctness** | `CREATE TABLE IF NOT EXISTS` and index creation need a real database to validate |

Docker-based tests give you **confidence that your code works end-to-end** against real infrastructure, while Testcontainers handles container lifecycle automatically — no manual Docker commands needed.

---

## Technology Stack

| Component | Package | Version | Purpose |
|-----------|---------|---------|---------|
| **Test Framework** | `xunit` | 2.9.3 | Test runner and assertions |
| **Assertions** | `FluentAssertions` | 6.12.2 | Readable assertion syntax |
| **PostgreSQL Container** | `Testcontainers.PostgreSql` | 4.11.0 | Manages PostgreSQL Docker container |
| **EventStoreDB Container** | `Testcontainers.KurrentDb` | 4.11.0 | Manages EventStoreDB Docker container |
| **PostgreSQL Client** | `Npgsql` | 8.0.6 | .NET PostgreSQL driver |
| **Micro ORM** | `Dapper` | 2.1.35 | Lightweight SQL mapping |
| **EventStore Client** | `EventStore.Client.Grpc.Streams` | 23.3.9 | gRPC client for EventStoreDB |
| **Test SDK** | `Microsoft.NET.Test.Sdk` | 17.12.0 | .NET test infrastructure |

---

## Test Infrastructure Pattern

### xUnit Collection Fixtures

Both test projects use the **xUnit Collection Fixture** pattern. This is a critical architectural decision:

```
┌──────────────────────────────────────────────────────────┐
│ xUnit Test Collection (e.g., "PostgreSql")               │
│                                                          │
│  ┌─────────────────┐    shared instance    ┌───────────┐ │
│  │ PostgreSqlFixture│◄────────────────────►│ Docker    │ │
│  │ (IAsyncLifetime) │                      │ Container │ │
│  └────────┬────────┘                      └───────────┘ │
│           │                                              │
│     injected into                                        │
│    ┌──────┼──────────────────────┐                       │
│    │      │                     │                        │
│    ▼      ▼                     ▼                        │
│  Test   Test                  Test                       │
│ Class A  Class B              Class N                    │
│                                                          │
│  All test classes share ONE container instance.           │
│  Container starts once, runs all tests, then stops.      │
└──────────────────────────────────────────────────────────┘
```

**Why this pattern?**
- Starting a Docker container takes 2-5 seconds. Starting one per test class would make the suite unbearably slow.
- All tests in the collection share one container, but each test uses a **unique stream/account ID** (`Guid.NewGuid()`) to prevent interference.
- `IAsyncLifetime` ensures proper async startup (`InitializeAsync`) and teardown (`DisposeAsync`).

### Container Lifecycle

```
Test Run Start
  │
  ├─► Fixture Constructor: configure container (image, ports, env vars)
  ├─► InitializeAsync(): start container, create client/connection, run schema migrations
  │
  ├─► Test 1 executes (unique stream ID)
  ├─► Test 2 executes (unique stream ID)
  ├─► ...
  ├─► Test N executes (unique stream ID)
  │
  └─► DisposeAsync(): dispose client, stop and remove container
```

---

## PostgreSQL Integration Tests

**Project:** `src/BullOak.Test.PostgreSql.Integration/`
**Docker Image:** `postgres:16-alpine`

These tests validate BullOak's `PostgreSqlEventSourcedRepository` — the production-ready event store adapter that persists events as JSONB rows in PostgreSQL.

### PostgreSQL Infrastructure Setup

**File:** `PostgreSqlFixture.cs`

```csharp
// 1. Configure the container
_container = new PostgreSqlBuilder("postgres:16-alpine")
    .WithDatabase("bulloak_test")
    .WithUsername("test")
    .WithPassword("test")
    .Build();

// 2. Start container and create schema
await _container.StartAsync();
ConnectionString = _container.GetConnectionString();
DataSource = NpgsqlDataSource.Create(ConnectionString);
await PostgreSqlEventStoreSchema.EnsureSchemaAsync(DataSource);
```

**What `EnsureSchemaAsync` creates:**
```sql
CREATE TABLE IF NOT EXISTS events (
    global_position   BIGSERIAL       PRIMARY KEY,
    stream_id         TEXT            NOT NULL,
    stream_position   BIGINT          NOT NULL,
    event_type        TEXT            NOT NULL,
    event_data        JSONB           NOT NULL,
    created_at        TIMESTAMPTZ     NOT NULL DEFAULT now(),
    CONSTRAINT uq_events_stream_position UNIQUE (stream_id, stream_position)
);

CREATE INDEX IF NOT EXISTS ix_events_stream_id_position
    ON events (stream_id, stream_position);
```

**Collection Definition:** `PostgreSqlCollection.cs` — binds the `"PostgreSql"` collection name to the fixture.

### PostgreSQL Domain Model

**File:** `Events.cs` — Domain events and state used across all PostgreSQL tests.

| Type | Properties | Role |
|------|-----------|------|
| `AccountOpened` | `AccountId`, `OwnerName`, `InitialDeposit` | First event in the stream; initializes the account |
| `MoneyDeposited` | `Amount`, `Description` | Increases the balance |
| `MoneyWithdrawn` | `Amount`, `Description` | Decreases the balance |
| `AccountState` | `AccountId`, `OwnerName`, `Balance`, `TransactionCount` | Aggregate state, reconstructed by replaying events |

**File:** `Appliers.cs` — Event appliers that tell BullOak how each event transforms state.

```
AccountOpened  → sets AccountId, OwnerName, Balance = InitialDeposit, TransactionCount = 1
MoneyDeposited → Balance += Amount, TransactionCount++
MoneyWithdrawn → Balance -= Amount, TransactionCount++
```

### PostgreSQL Test Catalog

Each test in `PostgreSqlRepositoryTests.cs` is documented below with its purpose, what it teaches about event sourcing, and what BullOak behavior it validates.

---

#### 1. `NewStream_IsNewState_ShouldBeTrue`

**Purpose:** Verify that opening a session for a non-existent stream correctly reports it as "new."

**What it teaches:**
- When you call `BeginSessionFor` with an ID that has no events, BullOak creates an empty default state.
- `session.IsNewState` returns `true` — this is how your application logic can distinguish between "creating a new aggregate" and "loading an existing one."

**Workflow:**
```
BeginSessionFor("account-<guid>") → session.IsNewState == true
```

---

#### 2. `WriteAndReadSingleEvent_ShouldRehydrateState`

**Purpose:** The fundamental round-trip test — write one event, then read it back and verify state reconstruction.

**What it teaches:**
- **Write path:** `session.AddEvent(event)` records the event, then `session.SaveChanges()` persists it to PostgreSQL as a JSONB row.
- **Read path (Rehydration):** When you call `BeginSessionFor` again, BullOak reads all events from the `events` table for that `stream_id`, deserializes them, and replays them through your appliers to reconstruct state.
- This is the core event sourcing loop: **store events, not state**.

**Workflow:**
```
Session 1: AddEvent(AccountOpened) → SaveChanges() → events table gets 1 row
Session 2: BeginSessionFor(same ID) → BullOak reads row → deserializes → applies → state reconstructed
           state.Balance == 100, state.OwnerName == "Alice"
```

---

#### 3. `WriteMultipleEvents_ShouldRehydrateInOrder`

**Purpose:** Verify that multiple events written in a single session are stored and replayed in the correct order.

**What it teaches:**
- Event ordering is guaranteed by `stream_position` (monotonically increasing per stream).
- During rehydration, events are replayed **in the order they were stored**, producing the correct final state.
- The balance calculation `500 + 200 - 50 = 650` only works if events are applied in the right order.

**Workflow:**
```
Session 1: AddEvent(AccountOpened, 500) → AddEvent(MoneyDeposited, 200) → AddEvent(MoneyWithdrawn, 50) → SaveChanges()
Session 2: BeginSessionFor → rehydrate → Balance = 500 + 200 - 50 = 650 ✓
```

---

#### 4. `AppendToExistingStream_ShouldWork`

**Purpose:** Verify that new events can be appended to an existing stream across separate sessions.

**What it teaches:**
- Event streams are **append-only** — new events are added after existing ones, never inserted or overwritten.
- BullOak tracks the `stream_position` (concurrency ID) internally, so the second session knows to start at position 1.
- This models real-world usage: create an account today, deposit money tomorrow — each operation is a separate session.

**Workflow:**
```
Session 1: AddEvent(AccountOpened, 100) → SaveChanges()      → stream_position 0
Session 2: AddEvent(MoneyDeposited, 50) → SaveChanges()       → stream_position 1
Session 3: BeginSessionFor → rehydrate → Balance = 100 + 50 = 150 ✓
```

---

#### 5. `ConcurrencyConflict_ShouldThrowConcurrencyException`

**Purpose:** Verify that optimistic concurrency control prevents lost updates when two sessions conflict.

**What it teaches:**
- **Optimistic concurrency** is a core event sourcing guarantee. When two sessions load the same stream simultaneously, both see the same `stream_position`. The first to save succeeds; the second detects the conflict and throws `ConcurrencyException`.
- This is enforced by the `UNIQUE (stream_id, stream_position)` constraint in PostgreSQL — if session B tries to insert at a position that session A already wrote, the database rejects it.
- This prevents the classic "lost update" problem without needing locks.

**Workflow:**
```
Session A loads stream (position 0)
Session B loads stream (position 0)     ← both see same state
Session A: AddEvent + SaveChanges()     → writes at position 1 ✓
Session B: AddEvent + SaveChanges()     → tries position 1 → UNIQUE violation → ConcurrencyException ✗
```

**Real-world analogy:** Two bank tellers processing withdrawals from the same account simultaneously. Without concurrency control, both could approve a withdrawal that overdrafts the account.

---

#### 6. `ThrowIfNotExists_WithEmptyStream_ShouldThrow`

**Purpose:** Verify that `throwIfNotExists: true` correctly throws `StreamNotFoundException` for non-existent streams.

**What it teaches:**
- The `throwIfNotExists` parameter changes BullOak's behavior from "create a new stream if it doesn't exist" to "fail fast if it doesn't exist."
- This is useful for operations that should only apply to existing aggregates (e.g., depositing money into an account that must already be open).

---

#### 7. `ThrowIfNotExists_WithExistingStream_ShouldNotThrow`

**Purpose:** Verify that `throwIfNotExists: true` succeeds when the stream actually exists.

**What it teaches:**
- Complements test #6 — confirms the flag only throws for genuinely missing streams, not as a false positive.

---

#### 8. `Contains_WithNoStream_ShouldReturnFalse` / `Contains_WithExistingStream_ShouldReturnTrue`

**Purpose:** Verify the `Contains(streamId)` method checks stream existence without loading events.

**What it teaches:**
- `Contains` is a lightweight existence check — it queries the database without reading/deserializing any events.
- Useful for validation logic: "does this account exist?" before performing an operation.

---

#### 9. `Delete_ShouldRemoveAllEvents`

**Purpose:** Verify that `Delete(streamId)` removes all events for a stream.

**What it teaches:**
- Although event sourcing is typically append-only, BullOak supports stream deletion for scenarios like GDPR "right to be forgotten" or test cleanup.
- After deletion, `Contains` returns `false` and `BeginSessionFor` returns a new/empty state.

---

#### 10. `AppliesAt_ShouldFilterEventsByTimestamp`

**Purpose:** Verify point-in-time queries — loading state as it was at a specific moment.

**What it teaches:**
- **Temporal queries** are a key advantage of event sourcing. By passing `appliesAt: cutoffTime`, BullOak only replays events with `created_at <= cutoffTime`.
- This enables "what was the balance on March 10th?" queries without maintaining separate snapshots.
- The test writes two events with a time gap, then loads state at the midpoint — only the first event should be visible.

**Workflow:**
```
Time T1: AccountOpened (balance: 100)
         ← cutoffTime recorded here →
Time T2: MoneyDeposited (balance: +50)

Load with appliesAt = cutoffTime → only see AccountOpened → Balance = 100 ✓
Load without appliesAt           → see both events        → Balance = 150
```

---

#### 11. `SaveChangesCalledTwice_ShouldAppendCorrectly`

**Purpose:** Verify that calling `SaveChanges()` multiple times within the same session correctly appends events.

**What it teaches:**
- A session can be used for multiple save operations. After the first `SaveChanges()`, you can add more events and save again.
- Each save correctly increments the `stream_position`.

---

#### 12. `DisposeWithoutSave_ShouldNotPersistEvents`

**Purpose:** Verify that disposing a session without calling `SaveChanges()` discards uncommitted events.

**What it teaches:**
- The session acts as a **unit of work**. If you don't explicitly call `SaveChanges()`, nothing is persisted.
- This is the "read-only session" pattern — load state, inspect it, dispose without saving.
- Also useful for error scenarios: if business validation fails after adding events, simply dispose to discard them.

---

## EventStoreDB Integration Tests

**Project:** `src/BullOak.Test.EventStore.Integration/`
**Docker Image:** `eventstore/eventstore:latest`

These tests demonstrate both raw EventStoreDB operations and BullOak's integration with EventStoreDB as an external event store.

### EventStoreDB Infrastructure Setup

**File:** `EventStoreFixture.cs`

```csharp
_container = new KurrentDbBuilder("eventstore/eventstore:latest")
    .WithPortBinding(KurrentDbBuilder.KurrentDbPort, false)  // random host port
    .WithEnvironment("EVENTSTORE_INSECURE", "true")          // no TLS for tests
    .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")         // single node
    .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "All")    // enable projections
    .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "true")  // $by_category, $by_event_type
    .WithEnvironment("EVENTSTORE_MEM_DB", "true")            // in-memory, no disk
    .Build();
```

**Key configuration choices:**
- **Insecure mode:** Disables TLS — simpler for testing, never use in production.
- **Cluster size 1:** Single-node mode — faster startup, no consensus needed.
- **All projections:** Enables server-side projections needed by `ProjectionTests`.
- **In-memory database:** Events are stored in RAM only — faster, no disk cleanup needed.
- **Random port binding:** Prevents port conflicts when running tests in parallel or on CI.

**Connection string format:** `esdb://{host}:{port}?tls=false`

### EventStoreDB Domain Models

**File:** `Events.cs` — Order domain events used in basic and projection/subscription tests.

| Event | Properties | Meaning |
|-------|-----------|---------|
| `OrderCreated` | `OrderId`, `CustomerName`, `CreatedAt` | A new order was placed |
| `ItemAddedToOrder` | `OrderId`, `ProductName`, `Quantity`, `UnitPrice` | An item was added to an order |
| `OrderCompleted` | `OrderId`, `CompletedAt` | An order was fulfilled |

**File:** `BullOakIntegrationTests.cs` — Account domain events used for BullOak integration.

| Event | Properties | Meaning |
|-------|-----------|---------|
| `AccountOpened` | `AccountId`, `HolderName`, `InitialDeposit` | A bank account was opened |
| `MoneyDeposited` | `AccountId`, `Amount` | Money was deposited |
| `MoneyWithdrawn` | `AccountId`, `Amount` | Money was withdrawn |

**State Interface:** `IAccountState` — BullOak uses `Reflection.Emit` to generate a concrete class at runtime.

### Basic EventStore Tests

**File:** `BasicEventStoreTests.cs`

These tests teach fundamental EventStoreDB operations — how to write, read, and manage event streams using the gRPC client. They do **not** use BullOak; they demonstrate the raw event store API.

---

#### 1. `WriteAndReadSingleEvent`

**Purpose:** The simplest possible test — write one event and read it back. Validates container connectivity.

**What it teaches:**
- **Event serialization:** Events are serialized to UTF-8 JSON bytes and wrapped in `EventData` with a UUID and type name.
- **Stream creation:** Writing to a stream that doesn't exist creates it implicitly (`StreamState.NoStream`).
- **Reading:** `ReadStreamAsync` returns a `IAsyncEnumerable<ResolvedEvent>` — events are streamed lazily.
- **Event type metadata:** The event type name (e.g., `"OrderCreated"`) is stored as metadata, separate from the serialized payload.

---

#### 2. `WriteMultipleEventsAndReadInOrder`

**Purpose:** Write a batch of 4 events and verify they come back in insertion order.

**What it teaches:**
- EventStoreDB is an **append-only log** — events are stored in the exact order they were written.
- Batch writes (passing an array of `EventData`) are atomic — either all events are written or none.
- The event type string lets you identify each event without deserializing the payload.

---

#### 3. `ConcurrencyConflictThrowsWrongExpectedVersion`

**Purpose:** Demonstrate EventStoreDB's native optimistic concurrency control.

**What it teaches:**
- When appending events, you specify the expected state of the stream (`StreamState.NoStream`, `StreamState.Any`, or a specific `StreamRevision`).
- If the stream's actual state doesn't match your expectation, EventStoreDB throws `WrongExpectedVersionException`.
- This is how EventStoreDB prevents lost updates — the same guarantee BullOak's PostgreSQL adapter provides via database constraints.

---

#### 4. `AppendWithExpectedRevision`

**Purpose:** Show explicit revision-based optimistic concurrency.

**What it teaches:**
- After writing N events, the stream's revision is N-1 (zero-based).
- To append more events, you pass the current revision. EventStoreDB verifies it matches before accepting the write.
- The `WriteResult.NextExpectedStreamRevision` tells you the new revision after a successful write.
- This is the building block for "read-then-write" patterns in event sourcing.

---

#### 5. `ReadStreamBackwards`

**Purpose:** Read events from the end of the stream (most recent first).

**What it teaches:**
- `Direction.Backwards` with `StreamPosition.End` reads from the latest event.
- `maxCount: 1` limits the read to just the most recent event.
- Useful for: getting the last event to determine current state quickly, paginating through history, finding the most recent event of a specific type.

---

#### 6. `ReadNonExistentStreamThrows`

**Purpose:** Verify error handling for missing streams.

**What it teaches:**
- Reading from a stream that has never been written to throws `StreamNotFoundException`.
- This is different from BullOak's behavior (which returns an empty/new state by default) — BullOak wraps this raw behavior with its `throwIfNotExists` parameter.

---

### BullOak Integration Tests

**File:** `BullOakIntegrationTests.cs`

These tests bridge BullOak's event sourcing framework with a real EventStoreDB. They demonstrate how you would build a custom EventStoreDB-backed adapter for BullOak.

---

#### 1. `RehydrateStateFromEventStore`

**Purpose:** The core integration test — write events to EventStoreDB, read them back, and use BullOak's rehydrator to reconstruct state.

**What it teaches:**
- **The bridge pattern:** Events stored in EventStoreDB must be converted to BullOak's `StoredEvent` format (type + object + position index).
- **Assembly-scanned appliers:** `WithAnyAppliersFrom(Assembly.GetExecutingAssembly())` discovers `AccountStateApplier` automatically.
- **Interface-based state:** `IAccountState` is a C# interface; BullOak generates a concrete implementation at runtime using IL emission.
- **State rehydration:** `configuration.StateRehydrator.RehydrateFrom<IAccountState>(storedEvents)` replays all events through the appliers.

**Workflow:**
```
Write to EventStoreDB:
  AccountOpened(100) → MoneyDeposited(50) → MoneyWithdrawn(30) → MoneyDeposited(200)

Read from EventStoreDB → Convert to StoredEvent[] → BullOak rehydrates:
  state.Balance = 100 + 50 - 30 + 200 = 320 ✓
  state.TransactionCount = 4 ✓
```

---

#### 2. `RehydrateStateAsyncFromEventStore`

**Purpose:** Same as above but using `IAsyncEnumerable<StoredEvent>` for lazy streaming.

**What it teaches:**
- In production, you don't want to load all events into memory at once. `IAsyncEnumerable` lets you stream events one at a time.
- BullOak's rehydrator supports both sync (`StoredEvent[]`) and async (`IAsyncEnumerable<StoredEvent>`) input.
- The `yield return` pattern creates a pipeline: read from EventStoreDB → convert → feed to BullOak, one event at a time.

---

#### 3. `RoundTrip_WriteWithBullOak_ReadFromEventStore`

**Purpose:** Full round-trip — write events through BullOak's session, persist to EventStoreDB, read back and rehydrate.

**What it teaches:**
- **Write side:** BullOak's `InMemoryEventSourcedRepository` + sessions provide a clean API for building aggregates. Events flow through appliers immediately, so you can make decisions based on updated state within the same session.
- **Persistence bridge:** After saving to the in-memory repo, extract events and write them to EventStoreDB. In production, you'd have a dedicated `EventStoreDbRepository` that does this in one step.
- **Read side:** Events from EventStoreDB are deserialized and fed back to BullOak's rehydrator, producing identical state.
- **Type registry:** The `DeserializeEvent` helper maps event type names (strings) back to CLR types. In production, use a proper type registry or convention-based mapper.

---

### Projection Tests

**File:** `ProjectionTests.cs`

Projections are server-side functions running inside EventStoreDB that process events and create new derived streams. These tests validate the built-in system projections.

> **Note:** Projection tests use a **3000ms delay** for eventual consistency. EventStoreDB projections process events asynchronously — they may not be immediately visible after writing.

---

#### 1. `ByCategoryProjection_GroupsStreamsByPrefix`

**Purpose:** Demonstrate the `$by_category` system projection.

**What it teaches:**
- EventStoreDB uses the stream name prefix (everything before the first `-`) as the category.
- Streams `order-{guid1}` and `order-{guid2}` both belong to category `order`.
- The `$by_category` projection creates a virtual stream `$ce-order` containing links to all events in `order-*` streams.
- `resolveLinkTos: true` is critical — without it, you get link events instead of the actual event data.
- **Use case:** Build a dashboard showing "all orders across all customers" by reading from `$ce-order`.

**Workflow:**
```
order-{id1}: [OrderCreated(Alice)]
order-{id2}: [OrderCreated(Bob)]
                    │
        $by_category projection
                    │
                    ▼
$ce-order: [link → OrderCreated(Alice), link → OrderCreated(Bob)]
```

---

#### 2. `ByEventTypeProjection_GroupsEventsByType`

**Purpose:** Demonstrate the `$by_event_type` system projection.

**What it teaches:**
- The `$by_event_type` projection creates a stream `$et-{EventTypeName}` for each event type.
- All `OrderCompleted` events across all streams end up in `$et-OrderCompleted`.
- **Use case:** "Show me every order that was completed today" — read from `$et-OrderCompleted` with a time filter.

---

#### 3. `ReadAllWithFilter_CountsOrdersPerCustomer`

**Purpose:** Client-side projection using the `$all` stream with event type filtering.

**What it teaches:**
- The `$all` stream contains every event from every stream in the database.
- `EventTypeFilter.Prefix("OrderCreated")` filters server-side, reducing network traffic.
- This is a **client-side projection** pattern: read filtered events from `$all` and aggregate them in your application code.
- Often simpler and more reliable than custom server-side JavaScript projections.
- **Use case:** Build a "customer → order count" read model by subscribing to `$all` filtered by `OrderCreated` events.

**Workflow:**
```
$all stream (filtered by OrderCreated):
  OrderCreated(Alice) → OrderCreated(Bob) → OrderCreated(Alice)

Client-side aggregation:
  { "Alice": 2, "Bob": 1 }
```

---

### Subscription Tests

**File:** `SubscriptionTests.cs`

Subscriptions push events to your application as they are written — essential for building reactive systems, read models, and event-driven architectures.

---

#### 1. `SubscribeToStream_ReceivesNewEvents`

**Purpose:** Subscribe to a specific stream and receive events as they arrive.

**What it teaches:**
- `SubscribeToStream` with `FromStream.Start` catches up from the beginning, then continues live.
- The subscription is an `IAsyncEnumerable<ResolvedEvent>` — you consume events in a loop.
- A `CancellationTokenSource` with a timeout prevents the test from hanging if events don't arrive.
- **Use case:** Watch a specific aggregate for changes (e.g., update a UI when an order is modified).

---

#### 2. `SubscribeToStream_FromEnd_ReceivesOnlyNewEvents`

**Purpose:** Subscribe from the end of a stream — only receive future events.

**What it teaches:**
- `FromStream.End` skips all existing events and only delivers new ones written after the subscription starts.
- The subscription is consumed in a background task because we need to write new events after subscribing.
- A small delay (`Task.Delay(500)`) ensures the subscription is established before writing.
- **Use case:** Real-time notifications — "alert me when a new deposit is made to this account."

**Workflow:**
```
Time 1: OrderCreated is written (before subscription)
Time 2: Subscription starts from END
Time 3: ItemAddedToOrder is written (after subscription)

Subscription receives: [ItemAddedToOrder] only — NOT OrderCreated
```

---

#### 3. `SubscribeToAll_WithTypeFilter_ReceivesFilteredEvents`

**Purpose:** Subscribe to the `$all` stream with event type filtering.

**What it teaches:**
- `SubscribeToAll` watches every stream in the database — powerful for cross-aggregate projections.
- `SubscriptionFilterOptions` with `EventTypeFilter.Prefix(...)` filters events server-side, so your app only receives relevant events.
- **Use case:** Build a read model that reacts to `OrderCompleted` events from any stream — e.g., update a "completed orders" dashboard.

---

## CI/CD Pipeline

**File:** `.github/workflows/ci.yml`

The CI pipeline runs all test types in parallel after a successful build:

```
                        ┌─► Unit Tests
                        │
Build ──────────────────┼─► Acceptance Tests
                        │
                        ├─► End-to-End Tests
                        │
                        ├─► EventStore Integration Tests (Docker)
                        │
                        └─► PostgreSQL Integration Tests (Docker)
```

**Docker in CI:**
- GitHub Actions runners have Docker pre-installed.
- The pipeline pre-pulls Docker images (`docker pull`) before running tests, ensuring consistent behavior.
- Testcontainers handles container lifecycle within the test process — no `docker-compose` needed.
- Test results are uploaded as TRX artifacts with 30-day retention.

---

## Running Tests Locally

### Prerequisites

1. **Docker Desktop** must be running
2. **.NET 8.0 SDK** installed
3. Sufficient disk space for Docker images (~500MB for PostgreSQL, ~800MB for EventStoreDB)

### Run All Integration Tests

```bash
# PostgreSQL integration tests
cd src/BullOak.Test.PostgreSql.Integration
dotnet test --verbosity normal

# EventStoreDB integration tests
cd src/BullOak.Test.EventStore.Integration
dotnet test --verbosity normal
```

### Run Specific Tests

```bash
# Run a single test
dotnet test --filter "WriteAndReadSingleEvent_ShouldRehydrateState"

# Run all tests in a specific class
dotnet test --filter "FullyQualifiedName~PostgreSqlRepositoryTests"
dotnet test --filter "FullyQualifiedName~ProjectionTests"
```

### Pre-pull Docker Images (Optional, Faster First Run)

```bash
docker pull postgres:16-alpine
docker pull eventstore/eventstore:latest
```

---

## Troubleshooting

| Problem | Cause | Solution |
|---------|-------|----------|
| `Cannot connect to the Docker daemon` | Docker Desktop not running | Start Docker Desktop and wait for it to be ready |
| Tests hang for 60+ seconds then fail | Container failed to start; port conflict or resource issue | Check `docker ps` for orphaned containers; restart Docker |
| `StreamNotFoundException` in projection tests | Projection hasn't processed events yet | Increase the `Task.Delay` (default: 3000ms) |
| `ConcurrencyException` in unexpected places | Test isolation failure — two tests using the same stream ID | Ensure every test uses `Guid.NewGuid()` in stream names |
| `NpgsqlException: connection refused` | PostgreSQL container not ready | Testcontainers has built-in health checks; if this happens, the container may have crashed |
| Tests pass locally but fail in CI | Docker image version difference | Pin image versions (e.g., `postgres:16-alpine` not `postgres:latest`) |

---

## PostgreSQL Read Model Projection Tests

**Project:** `src/BullOak.Test.ReadModel.Integration/`
**Docker Image:** `postgres:16-alpine`

These tests demonstrate the CQRS (Command Query Responsibility Segregation) read model pattern — the most practical reason to combine event sourcing with a relational database like PostgreSQL.

### Why Read Models?

In event sourcing, the event store is optimized for writes (append-only, per-stream). But many real-world queries are impossible or expensive against an event store:

| Query | Event Store Approach | Read Model Approach |
|-------|---------------------|-------------------|
| "What is Alice's balance?" | Read all events for Alice's stream, replay to get state | `SELECT balance FROM account_summary WHERE account_id = 'alice'` |
| "Find all accounts with balance > 1000" | Read ALL streams, replay ALL events, filter | `SELECT * FROM account_summary WHERE balance > 1000` |
| "Show last 5 transactions for account X" | Read stream backwards, take 5 | `SELECT * FROM transaction_history WHERE account_id = 'X' ORDER BY id DESC LIMIT 5` |

Read models solve this by maintaining **denormalized, query-optimized tables** that are populated by projecting events. The key insight: read models are **disposable** — you can always delete them and rebuild from events.

```
┌────────────────────────────────────────────────────┐
│                  PostgreSQL Database                │
│                                                    │
│  ┌──────────────┐       ┌──────────────────────┐  │
│  │ events table  │       │ account_summary      │  │
│  │ (WRITE side)  │──────►│ transaction_history   │  │
│  │              │  proj  │ (READ side)           │  │
│  └──────────────┘ ector  └──────────────────────┘  │
│                                                    │
│  Source of truth         Disposable, rebuildable    │
│  Append-only             Query-optimized            │
│  Per-stream              Cross-stream queries OK    │
└────────────────────────────────────────────────────┘
```

### Read Model Infrastructure Setup

**File:** `ReadModelFixture.cs`

The fixture creates a PostgreSQL container with **both** schemas:
1. The BullOak event store schema (`events` table) — the write side
2. The read model schema (`account_summary`, `transaction_history`) — the read side

```csharp
_container = new PostgreSqlBuilder("postgres:16-alpine")
    .WithDatabase("bulloak_readmodel_test")
    .WithUsername("test")
    .WithPassword("test")
    .Build();

// Create BOTH schemas in the same database
await PostgreSqlEventStoreSchema.EnsureSchemaAsync(DataSource);  // write side
await ReadModelSchema.EnsureSchemaAsync(DataSource);              // read side
```

**Read Model Schema** (`ReadModelSchema.cs`):

```sql
-- Current state snapshot — one row per account
CREATE TABLE account_summary (
    account_id    TEXT            PRIMARY KEY,
    owner_name    TEXT            NOT NULL,
    balance       DECIMAL(18,2)  NOT NULL DEFAULT 0,
    tx_count      INT            NOT NULL DEFAULT 0,
    last_updated  TIMESTAMPTZ    NOT NULL DEFAULT now()
);

-- Append-only transaction log — one row per event
CREATE TABLE transaction_history (
    id            BIGSERIAL      PRIMARY KEY,
    account_id    TEXT           NOT NULL,
    event_type    TEXT           NOT NULL,
    amount        DECIMAL(18,2) NOT NULL DEFAULT 0,
    description   TEXT           NOT NULL DEFAULT '',
    occurred_at   TIMESTAMPTZ   NOT NULL DEFAULT now()
);
```

### The Projector Pattern

**File:** `AccountReadModelProjector.cs`

The projector is the bridge between events and read models. It receives domain events and transforms them into SQL operations:

```
AccountOpened  → INSERT INTO account_summary (UPSERT)
               → INSERT INTO transaction_history
MoneyDeposited → UPDATE account_summary SET balance = balance + @Amount
               → INSERT INTO transaction_history
MoneyWithdrawn → UPDATE account_summary SET balance = balance - @Amount
               → INSERT INTO transaction_history
```

Key design decisions:
- **UPSERT pattern** (`ON CONFLICT DO UPDATE`): Makes projections idempotent — replaying the same event twice produces the same result.
- **Incremental updates**: Each event modifies the read model incrementally (`balance + @Amount`), not by rebuilding from scratch.
- **Multi-table projection**: One event can update multiple tables simultaneously.

### Read Model Test Catalog

Each test in `ReadModelProjectionTests.cs` demonstrates a different aspect of the CQRS read model pattern.

---

#### 1. `ProjectEventToReadModel_ShouldCreateDenormalizedRow`

**Purpose:** The fundamental CQRS flow — write an event through BullOak, project it into the read model, and query with plain SQL.

**What it teaches:**
- The three-step CQRS flow: **Write** (BullOak persists to events table) → **Project** (projector INSERTs into account_summary) → **Query** (Dapper reads the denormalized row).
- Read model rows contain **pre-computed values** (balance, owner_name) that would otherwise require replaying events.
- Dapper maps PostgreSQL snake_case columns directly to C# DTOs.

**Workflow:**
```
BullOak: AddEvent(AccountOpened) → SaveChanges() → events table row
Projector: ProjectAccountOpened() → account_summary row
Query: SELECT balance, owner_name FROM account_summary → {500, "Alice"}
```

---

#### 2. `MultipleEvents_ShouldUpdateReadModelIncrementally`

**Purpose:** Verify that each event updates the read model incrementally, not by rebuilding from scratch.

**What it teaches:**
- **Incremental projection** is the production pattern: the projector processes events one at a time and applies small SQL updates (`SET balance = balance + @Amount`).
- This is much more efficient than replaying all events to compute state for each update.
- The balance after 3 events: `1000 + 250 - 75 = 1175`, with `tx_count = 3`.

---

#### 3. `ReadModel_ShouldBeQueryableWithSql`

**Purpose:** Demonstrate cross-stream queries that would be impossible against an event store.

**What it teaches:**
- The **killer feature** of read models: SQL queries across all aggregates. "Find all accounts with balance > 1000" is a simple `WHERE` clause — no need to replay events from every stream.
- Creates 3 accounts with different balances (5000, 500, 50) and queries for high-balance ones.
- Test isolation uses unique suffixes to prevent interference with other tests.

**Workflow:**
```
Create 3 accounts → Project each → SQL: WHERE balance > 1000 → Returns only "Rich" account
```

---

#### 4. `ReadModel_ShouldSupportMultipleTables`

**Purpose:** Demonstrate that one event stream can project into multiple read model tables.

**What it teaches:**
- **One source, many views**: The same events project into both `account_summary` (current state) and `transaction_history` (full log).
- Different tables serve different query patterns:
  - "What is my balance?" → `account_summary` (single row lookup)
  - "Show my recent transactions" → `transaction_history` (paginated list)
- This is extensible — you could add more tables (e.g., `monthly_totals`) without changing the event store.

**Workflow:**
```
3 events → Project to both tables →
  account_summary: balance=450, tx_count=3
  transaction_history: 3 rows with event_type, amount, description
```

---

#### 5. `ReadModel_ShouldHandleReplayFromScratch`

**Purpose:** Prove that read models are disposable — they can be destroyed and rebuilt from events.

**What it teaches:**
- This is one of the **most powerful properties** of event sourcing. The read model is NOT the source of truth — events are.
- The test: writes events → projects → verifies → **DROPS all read model tables** → recreates schema → re-projects → verifies the result is identical.
- Real-world use cases:
  - **Bug fix**: Fix a projection bug, rebuild the read model, done.
  - **New feature**: Add a new read model table, backfill from existing events.
  - **Disaster recovery**: Read model database corrupted? Rebuild from events.
  - **Schema migration**: Drop old table, create new one, replay.

**Workflow:**
```
Events → Project → Balance=225 ✓
  ↓
DROP all read model tables (simulates corruption)
  ↓
account_summary is EMPTY
  ↓
BullOak rehydrates from events table → state.Balance still 225 (events survive!)
  ↓
Re-project from events → Balance=225 ✓ (identical to original)
```

---

#### 6. `EventStoreAndReadModel_ShouldBeConsistent`

**Purpose:** Verify consistency between the write side (event store + BullOak rehydration) and the read side (read model).

**What it teaches:**
- Both sides must agree on the current state. This test saves events across **multiple sessions** (simulating operations over time), projects each batch, and verifies both sides produce `balance = 1300`.
- Write side verification: BullOak rehydrates from events → `state.Balance == 1300`.
- Read side verification: SQL query on `account_summary` → `balance == 1300`.
- This is the fundamental CQRS consistency guarantee.

---

## RabbitMQ + MassTransit Integration Tests

**Project:** `src/BullOak.Test.RabbitMq.Integration/`
**Docker Image:** `rabbitmq:3-management-alpine`

These tests demonstrate event publishing — after BullOak persists events, broadcasting them to external consumers via RabbitMQ and MassTransit.

### Why Event Publishing?

Event sourcing stores events for the write side, but other parts of your system need to react:

```
┌──────────────┐     persist     ┌──────────────────┐
│   BullOak    │────────────────►│   Event Store     │
│   Session    │                 │   (source of      │
│              │                 │    truth)          │
│   AddEvent() │                 └──────────────────┘
│   SaveChanges│
│              │     publish      ┌──────────────────┐     consume     ┌───────────────┐
│ IPublishEvents├────────────────►│    RabbitMQ       │───────────────►│  Consumer A    │
└──────────────┘                  │    (message       │                │  (read model)  │
                                  │     broker)       │                ├───────────────┤
                                  │                   │───────────────►│  Consumer B    │
                                  └──────────────────┘                │  (email)       │
                                                                      ├───────────────┤
                                                                      │  Consumer C    │
                                                                      │  (analytics)   │
                                                                      └───────────────┘
```

### RabbitMQ Infrastructure Setup

**File:** `RabbitMqFixture.cs`

```csharp
_container = new RabbitMqBuilder("rabbitmq:3-management-alpine")
    .WithUsername("guest")
    .WithPassword("guest")
    .Build();
```

**Key details:**
- **Management plugin included**: The `management-alpine` image includes the RabbitMQ management UI (port 15672) for debugging queues and exchanges.
- **AMQP port**: Testcontainers maps port 5672 (AMQP) to a random host port.
- **Lightweight**: The Alpine-based image is ~150MB.

### The Event Publisher Bridge

**File:** `MassTransitEventPublisher.cs`

This class implements BullOak's `IPublishEvents` interface and bridges events to RabbitMQ via MassTransit:

```csharp
public class MassTransitEventPublisher : IPublishEvents
{
    private readonly IBus _bus;

    public async Task Publish(ItemWithType @event, CancellationToken cancellationToken)
    {
        // MassTransit.Publish routes based on the runtime type
        await _bus.Publish(@event.instance, @event.type, cancellationToken);
    }
}
```

**How it's wired into BullOak:**
```csharp
var publisher = new MassTransitEventPublisher(bus);
var configuration = Configuration.Begin()
    // ...
    .WithEventPublisher(publisher)  // <-- THIS is the integration point
    // ...
    .Build();
```

When `SaveChanges()` is called, BullOak persists events first, then calls `IPublishEvents.Publish()` for each event. MassTransit handles serialization, exchange creation, and routing to consumers automatically.

**MassTransit topology (created automatically):**
```
Exchange: BullOak.Test.RabbitMq.Integration:AccountOpened (fanout)
    │
    ├──► Queue: account-opened-consumer
    │        └── Consumer: TestAccountOpenedConsumer
    │
    ├──► Queue: fanout-consumer-1
    │        └── Consumer: FanoutConsumer1
    │
    └──► Queue: fanout-consumer-2
             └── Consumer: FanoutConsumer2
```

### RabbitMQ Test Catalog

Each test in `RabbitMqPublishingTests.cs` demonstrates a different messaging pattern.

---

#### 1. `PublishEvent_ShouldBeReceivedByConsumer`

**Purpose:** The simplest MassTransit test — publish an event and verify a consumer receives it.

**What it teaches:**
- **MassTransit consumer pattern**: Implement `IConsumer<TMessage>` and register with `cfg.AddConsumer<T>()`.
- **Endpoint configuration**: `rabbitCfg.ReceiveEndpoint("queue-name", ...)` creates a named queue and binds it to the message type exchange.
- **Async coordination**: Uses `TaskCompletionSource<bool>` with a 10-second timeout to synchronize the test thread with the asynchronous consumer.
- **DI-based consumers**: MassTransit resolves consumer dependencies from the DI container.

**Workflow:**
```
Publish: AccountOpened { AccountId, OwnerName, InitialDeposit }
    ↓
RabbitMQ: Exchange → Queue "account-opened-consumer"
    ↓
Consumer: TestAccountOpenedConsumer.Consume() → adds to receivedEvents list
    ↓
Assert: receivedEvents[0].OwnerName == "Alice" ✓
```

---

#### 2. `BullOakSaveChanges_ShouldPublishEventsToRabbitMq`

**Purpose:** The full BullOak integration — saving events through a BullOak session automatically publishes them to RabbitMQ.

**What it teaches:**
- **The integration point**: `.WithEventPublisher(publisher)` in BullOak's configuration is all you need. After `SaveChanges()`, each event is published automatically.
- **Dual persistence**: Events are saved to the in-memory event store AND published to RabbitMQ. The event store is the source of truth; RabbitMQ is for notification.
- **Multi-type consumers**: `TestAllEventsConsumer` implements both `IConsumer<AccountOpened>` and `IConsumer<MoneyDeposited>` to handle different event types on one queue.
- **Expected count coordination**: The consumer signals completion after receiving the expected number of events.

**Workflow:**
```
BullOak Session:
  AddEvent(AccountOpened)     → applied to state immediately
  AddEvent(MoneyDeposited)    → applied to state immediately
  SaveChanges()               → persists to InMemoryRepo
                              → calls IPublishEvents.Publish() for each event
                              → MassTransit publishes to RabbitMQ
                                ↓
Consumer receives: [AccountOpened, MoneyDeposited] ✓
```

---

#### 3. `MultipleConsumers_ShouldAllReceiveEvents`

**Purpose:** Demonstrate the fan-out pattern — one event delivered to multiple independent consumers.

**What it teaches:**
- **Fan-out messaging**: MassTransit creates a fanout exchange per message type. When multiple queues are bound to the same exchange, every message goes to ALL queues.
- **Independent processing**: Each consumer has its own queue, events list, and completion signal. If one consumer fails, others are unaffected.
- **Keyed DI services**: Uses `[FromKeyedServices("consumer1")]` to inject different instances into different consumers — a clean pattern for test isolation.
- **Real-world use case**: One `AccountOpened` event → read model updater receives it AND notification service receives it AND analytics pipeline receives it.

**Workflow:**
```
Publish: AccountOpened { "Fanout-Test" }
    ↓
Exchange (fanout)
    ├──► Queue "fanout-consumer-1" → FanoutConsumer1 → consumer1Events ✓
    └──► Queue "fanout-consumer-2" → FanoutConsumer2 → consumer2Events ✓

Both consumers receive the SAME event independently.
```

---

#### 4. `DifferentEventTypes_ShouldRouteToCorrectConsumers`

**Purpose:** Demonstrate type-based routing — different event types go to different queues automatically.

**What it teaches:**
- **Type-safe routing**: MassTransit creates one exchange per message type. `AccountOpened` and `MoneyDeposited` are routed to different exchanges and therefore different queues.
- **No manual routing**: You don't configure routing keys, topic patterns, or message selectors. MassTransit handles everything based on the .NET type.
- **Separation of concerns**: Each consumer only sees the event types it cares about.

**Workflow:**
```
Publish AccountOpened → Exchange:AccountOpened → Queue "typed-account-events"
                                                    → TypedAccountOpenedConsumer ✓

Publish MoneyDeposited → Exchange:MoneyDeposited → Queue "typed-deposit-events"
                                                     → TypedMoneyDepositedConsumer ✓

Each consumer ONLY receives its event type.
```

---

## CI/CD Pipeline

**File:** `.github/workflows/ci.yml`

The CI pipeline runs all test types in parallel after a successful build:

```
                        ┌─► Unit Tests
                        │
                        ├─► Acceptance Tests
                        │
Build ──────────────────┼─► End-to-End Tests
                        │
                        ├─► EventStore Integration Tests (Docker)
                        │
                        ├─► PostgreSQL Integration Tests (Docker)
                        │
                        ├─► Read Model Integration Tests (Docker)
                        │
                        └─► RabbitMQ Integration Tests (Docker)
```

**Docker in CI:**
- GitHub Actions runners have Docker pre-installed.
- The pipeline pre-pulls Docker images (`docker pull`) before running tests, ensuring consistent behavior.
- Testcontainers handles container lifecycle within the test process — no `docker-compose` needed.
- Test results are uploaded as TRX artifacts with 30-day retention.

---

## Running Tests Locally

### Prerequisites

1. **Docker Desktop** must be running
2. **.NET 8.0 SDK** installed
3. Sufficient disk space for Docker images (~500MB for PostgreSQL, ~800MB for EventStoreDB, ~150MB for RabbitMQ)

### Run All Integration Tests

```bash
# PostgreSQL integration tests
cd src/BullOak.Test.PostgreSql.Integration
dotnet test --verbosity normal

# EventStoreDB integration tests
cd src/BullOak.Test.EventStore.Integration
dotnet test --verbosity normal

# Read Model projection tests (PostgreSQL + Dapper)
cd src/BullOak.Test.ReadModel.Integration
dotnet test --verbosity normal

# RabbitMQ + MassTransit tests
cd src/BullOak.Test.RabbitMq.Integration
dotnet test --verbosity normal
```

### Run Specific Tests

```bash
# Run a single test
dotnet test --filter "WriteAndReadSingleEvent_ShouldRehydrateState"

# Run all tests in a specific class
dotnet test --filter "FullyQualifiedName~PostgreSqlRepositoryTests"
dotnet test --filter "FullyQualifiedName~ReadModelProjectionTests"
dotnet test --filter "FullyQualifiedName~RabbitMqPublishingTests"
```

### Pre-pull Docker Images (Optional, Faster First Run)

```bash
docker pull postgres:16-alpine
docker pull eventstore/eventstore:latest
docker pull rabbitmq:3-management-alpine
```

---

## Troubleshooting

| Problem | Cause | Solution |
|---------|-------|----------|
| `Cannot connect to the Docker daemon` | Docker Desktop not running | Start Docker Desktop and wait for it to be ready |
| Tests hang for 60+ seconds then fail | Container failed to start; port conflict or resource issue | Check `docker ps` for orphaned containers; restart Docker |
| `StreamNotFoundException` in projection tests | Projection hasn't processed events yet | Increase the `Task.Delay` (default: 3000ms) |
| `ConcurrencyException` in unexpected places | Test isolation failure — two tests using the same stream ID | Ensure every test uses `Guid.NewGuid()` in stream names |
| `NpgsqlException: connection refused` | PostgreSQL container not ready | Testcontainers has built-in health checks; if this happens, the container may have crashed |
| Tests pass locally but fail in CI | Docker image version difference | Pin image versions (e.g., `postgres:16-alpine` not `postgres:latest`) |
| MassTransit consumer doesn't receive events | Consumer not yet bound to queue when event published | Increase `Task.Delay` after bus start (default: 1000ms) |
| `RabbitMQ.Client.Exceptions.BrokerUnreachableException` | RabbitMQ container not ready | Testcontainers handles readiness; check container logs if persistent |
