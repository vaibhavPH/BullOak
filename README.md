# BullOak

[![CI](https://github.com/vaibhavPH/BullOak/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/vaibhavPH/BullOak/actions/workflows/ci.yml)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

An event sourcing library for .NET that provides a complete framework for building event-sourced aggregates with automatic state reconstitution, optimistic concurrency, event upconversion, and a fluent configuration API.

---

## Table of Contents

- [What Is Event Sourcing?](#what-is-event-sourcing)
  - [Traditional (CRUD) vs Event Sourcing](#traditional-crud-vs-event-sourcing)
  - [A Real-World Analogy](#a-real-world-analogy)
  - [When Should You Use Event Sourcing?](#when-should-you-use-event-sourcing)
- [Why BullOak?](#why-bulloak)
- [Glossary](#glossary)
- [Quick Start — Console App Example](#quick-start--console-app-example)
- [Core Concepts](#core-concepts)
  - [Events](#events)
  - [State](#state)
  - [Appliers (Event Handlers)](#appliers-event-handlers)
  - [Configuration](#configuration)
  - [Sessions (Unit of Work)](#sessions-unit-of-work)
  - [Repository](#repository)
- [Architecture Overview](#architecture-overview)
  - [High-Level Data Flow](#high-level-data-flow)
  - [Class Hierarchy](#class-hierarchy)
- [Step-by-Step Tutorial: Bank Account](#step-by-step-tutorial-bank-account)
  - [1. Define Events](#1-define-events)
  - [2. Define State](#2-define-state)
  - [3. Write Appliers](#3-write-appliers)
  - [4. Configure BullOak](#4-configure-bulloak)
  - [5. Create the Repository](#5-create-the-repository)
  - [6. Use Sessions to Read and Write](#6-use-sessions-to-read-and-write)
- [A More Realistic Example: Cinema Reservation System](#a-more-realistic-example-cinema-reservation-system)
- [Advanced Features](#advanced-features)
  - [Interface-Based State (Dynamic Type Generation)](#interface-based-state-dynamic-type-generation)
  - [Event Upconversion (Schema Evolution)](#event-upconversion-schema-evolution)
  - [Event Publishing](#event-publishing)
  - [Event Interception (Middleware)](#event-interception-middleware)
  - [State Validation (Invariants)](#state-validation-invariants)
  - [Point-in-Time Queries](#point-in-time-queries)
  - [Optimistic Concurrency](#optimistic-concurrency)
  - [Delivery Guarantees](#delivery-guarantees)
- [Configuration Reference](#configuration-reference)
  - [Fluent Builder Chain](#fluent-builder-chain)
  - [Configuration Options](#configuration-options)
- [Integrating with External Event Stores](#integrating-with-external-event-stores)
- [Project Structure](#project-structure)
- [How It Works Internally](#how-it-works-internally)
  - [State Rehydration Flow](#state-rehydration-flow)
  - [SaveChanges Flow](#savechanges-flow)
  - [Dynamic Type Emission](#dynamic-type-emission)
  - [Applier Resolution and Caching](#applier-resolution-and-caching)
- [Key Types Reference](#key-types-reference)
- [Error Handling](#error-handling)
- [Running the Tests](#running-the-tests)
- [FAQ](#faq)
- [Contributing](#contributing)

---

## What Is Event Sourcing?

### Traditional (CRUD) vs Event Sourcing

In a traditional application, you store the **current state** of an entity — a row in a database table. When something changes, you overwrite that row. The previous values are lost forever.

```
Traditional (CRUD):

  Time 1:  INSERT BankAccount { id: "ACC-001", balance: 100, owner: "Alice" }
  Time 2:  UPDATE BankAccount SET balance = 150 WHERE id = "ACC-001"
  Time 3:  UPDATE BankAccount SET balance = 120 WHERE id = "ACC-001"
  Time 4:  UPDATE BankAccount SET balance = 320 WHERE id = "ACC-001"

  What you have in the database:  BankAccount { balance: 320 }
  What you've lost:               Why the balance changed, who changed it, when each change happened
```

In **event sourcing**, you never overwrite anything. Instead, you store an **append-only sequence of events** — facts about what happened. The current state is derived by replaying these events:

```
Event-sourced:

  Event 1:  AccountOpened   { initialDeposit: 100 }    → balance: 100
  Event 2:  MoneyDeposited  { amount: 50 }             → balance: 150
  Event 3:  MoneyWithdrawn  { amount: 30 }             → balance: 120
  Event 4:  MoneyDeposited  { amount: 200 }            → balance: 320

  What you have:  The complete history of every change
  Current state:  Replay all events → balance = 100 + 50 - 30 + 200 = 320
```

The key insight is that **events are immutable facts**. Once recorded, they are never modified or deleted. This gives you a complete, auditable history of everything that ever happened in your system.

### A Real-World Analogy

Think of a **bank statement**. Your bank doesn't store "your balance is $320." Instead, it stores every transaction:

```
Mar 1:  Opened account with $100
Mar 5:  Deposited $50 (paycheck)
Mar 10: Withdrew $30 (groceries)
Mar 15: Deposited $200 (freelance work)
```

Your current balance ($320) is derived from these transactions. If there's ever a dispute, the bank can trace exactly how you got to that number. This is event sourcing — your bank has been doing it for centuries.

### When Should You Use Event Sourcing?

Event sourcing is particularly valuable when:

- **Audit trails matter** — financial systems, healthcare, compliance-heavy domains where you must explain how you got to the current state
- **You need temporal queries** — "what was the account balance on March 10th?" becomes trivial
- **Multiple consumers need the same events** — one stream of events can feed multiple read models, analytics pipelines, and notification systems
- **Your domain is naturally event-driven** — order processing, workflow engines, IoT sensor data
- **Debugging complex state** — you can replay events to reconstruct exactly how a bug manifested

Event sourcing adds complexity, so it may be overkill for simple CRUD applications where you just need to store and retrieve data without caring about history.

---

## Why BullOak?

Implementing event sourcing from scratch requires significant boilerplate: you need to manage event storage, state reconstitution, concurrency control, event schema evolution, and the session lifecycle. BullOak handles all of this so you can focus purely on your domain logic.

Here is what BullOak provides and why each feature matters:

| Feature | What It Does | Why It Matters |
|---------|-------------|----------------|
| **Automatic state reconstitution** | When you load an aggregate, BullOak replays all stored events through your appliers to reconstruct the current state | You never manually iterate events and build state — BullOak does it for you |
| **Interface-based state** | Define state as a C# interface; BullOak generates a concrete class at runtime using IL emission with built-in read-only protection | Properties can only be set during event application, preventing accidental state mutation. This enforces the invariant that all state changes flow through events. |
| **Fluent configuration** | Type-safe builder chain that guides you through setup at compile time. Each step returns a different interface, so you can only call methods in the right sequence. | Impossible to misconfigure at compile time — the compiler catches mistakes before you run |
| **Event upconversion** | Transforms old event schemas into current ones at load time, without rewriting your event store | You can evolve your domain over months and years without data migrations |
| **Optimistic concurrency** | Detects when two sessions try to write to the same stream concurrently and throws `ConcurrencyException` | Prevents lost updates in multi-user or multi-thread scenarios |
| **Event publishing** | Hooks into the save lifecycle to publish events to external message buses (RabbitMQ, Kafka, etc.) | Enables event-driven architectures and CQRS read model projections |
| **Event interception** | Four lifecycle hooks per event (BeforePublish, AfterPublish, BeforeSave, AfterSave) | Cross-cutting concerns like logging, metrics, and auditing without polluting domain code |
| **State validation** | Enforces business invariants before events are persisted | Invalid state is caught at save time, not after the fact |
| **In-memory repository** | Complete, working repository implementation included | Great for unit tests, acceptance tests, and prototyping. For production, extend `BaseEventSourcedSession` to plug in any event store. |

---

## Glossary

These terms are used throughout this document and the BullOak codebase:

| Term | Definition |
|------|-----------|
| **Event** | An immutable fact describing something that happened. Examples: `AccountOpened`, `MoneyDeposited`. Once stored, events are never modified. |
| **Stream** | A named, ordered, append-only sequence of events belonging to one aggregate. Think of it as a partition key. Example: all events for account `ACC-001` live in one stream. |
| **Aggregate** | A cluster of domain objects treated as a single unit for data changes. In BullOak, each aggregate has its own event stream and state. |
| **State** | The current snapshot of an aggregate, derived by replaying all events in its stream. BullOak reconstructs state automatically; you never store it directly. |
| **Applier** | A function that defines how one event type transforms state. BullOak calls appliers during state reconstitution and when new events are added. Also known as an "event handler" or "projection function." |
| **Session** | BullOak's unit of work. Represents a single interaction with one aggregate: load state → make decisions → add events → save. Analogous to a database transaction. |
| **Repository** | Creates sessions and manages the connection to the underlying event store. BullOak includes an in-memory implementation; you extend `BaseEventSourcedSession` for production stores. |
| **Rehydration** | The process of reconstructing state from stored events. Also called "reconstitution" or "replay." |
| **Upconverter** | A transformer that converts old event schemas into current ones at load time, enabling schema evolution without data migration. |
| **Interceptor** | Middleware that hooks into the event lifecycle (before/after publish, before/after save) for cross-cutting concerns. |
| **`ItemWithType`** | BullOak's internal struct that pairs an event object (`instance`) with its runtime `Type`. Used throughout the pipeline because events are often handled as `object` but the runtime type matters for applier dispatch. |
| **`StoredEvent`** | A struct representing a persisted event: the event object, its type, and its position index in the stream. This is what gets passed to the rehydration pipeline. |
| **Concurrency ID** | The index of the last event seen when a session was loaded. Used for optimistic concurrency — if the stream has grown since loading, save will fail. |

---

## Quick Start — Console App Example

Here is a complete, working console application that demonstrates BullOak. You can copy this into a new .NET 8 project and run it immediately.

### 1. Create the project and add BullOak

```bash
dotnet new console -n BullOakDemo
cd BullOakDemo
```

Add a project reference to BullOak.Repositories (assuming you have the source locally):

```bash
dotnet add reference ../path/to/BullOak.Repositories/BullOak.Repositories.csproj
```

### 2. Program.cs

```csharp
using System.Reflection;
using BullOak.Repositories;
using BullOak.Repositories.Appliers;
using BullOak.Repositories.InMemory;

// ──────────────────────────────────────────────
// Step 1: Define your events
// ──────────────────────────────────────────────
// Events are simple classes that describe what happened.
// They are immutable facts — once stored, they never change.
// Use past tense for names: "AccountOpened", not "OpenAccount".

public record AccountOpened(string AccountId, decimal InitialDeposit);
public record MoneyDeposited(decimal Amount);
public record MoneyWithdrawn(decimal Amount);

// ──────────────────────────────────────────────
// Step 2: Define your state
// ──────────────────────────────────────────────
// State is what you get after replaying all events.
// Using a class here for simplicity. See "Interface-Based State"
// section for the more advanced (and recommended) approach.

public class AccountState
{
    public string AccountId { get; set; } = "";
    public decimal Balance { get; set; }
    public int TransactionCount { get; set; }
}

// ──────────────────────────────────────────────
// Step 3: Write appliers
// ──────────────────────────────────────────────
// Appliers define HOW each event type modifies state.
// Think of them as "fold" functions: (state, event) → newState
// BullOak discovers these automatically via assembly scanning.

public class AccountOpenedApplier : BaseApplyEvents<AccountState, AccountOpened>
{
    public override AccountState Apply(AccountState state, AccountOpened @event)
    {
        state.AccountId = @event.AccountId;
        state.Balance = @event.InitialDeposit;
        state.TransactionCount = 1;
        return state;
    }
}

public class MoneyDepositedApplier : BaseApplyEvents<AccountState, MoneyDeposited>
{
    public override AccountState Apply(AccountState state, MoneyDeposited @event)
    {
        state.Balance += @event.Amount;
        state.TransactionCount++;
        return state;
    }
}

public class MoneyWithdrawnApplier : BaseApplyEvents<AccountState, MoneyWithdrawn>
{
    public override AccountState Apply(AccountState state, MoneyWithdrawn @event)
    {
        state.Balance -= @event.Amount;
        state.TransactionCount++;
        return state;
    }
}

// ──────────────────────────────────────────────
// Step 4: Configure and run
// ──────────────────────────────────────────────

// Build the configuration — this wires up all the components BullOak needs.
// The fluent API enforces the correct order at compile time.
var configuration = Configuration.Begin()
    .WithDefaultCollection()       // use BullOak's built-in event collection
    .WithDefaultStateFactory()     // use BullOak's state factory (supports interfaces too)
    .NeverUseThreadSafe()          // no thread-safety needed for this demo
    .WithNoEventPublisher()        // no external message bus
    .WithAnyAppliersFrom(Assembly.GetExecutingAssembly())  // scan this assembly for appliers
    .AndNoMoreAppliers()
    .WithNoUpconverters()          // no event schema migrations
    .Build();

// Create an in-memory repository.
// The type parameters are: TId (stream identifier type), TState (aggregate state type).
var repo = new InMemoryEventSourcedRepository<string, AccountState>(configuration);

var accountId = "ACC-001";

// ── Create a new account ──
// BeginSessionFor creates a session. Since no events exist yet for this ID,
// BullOak creates an empty state (all default values).
using (var session = await repo.BeginSessionFor(accountId))
{
    Console.WriteLine($"Is new account? {session.IsNewState}");  // True

    // AddEvent does two things:
    // 1. Records the event for later persistence
    // 2. Immediately applies it to the in-memory state
    session.AddEvent(new AccountOpened(accountId, 100m));
    session.AddEvent(new MoneyDeposited(50m));

    // State is updated IMMEDIATELY after AddEvent — even before SaveChanges!
    // This is called "eager application" and lets you make decisions based
    // on the latest state within the same session.
    var state = session.GetCurrentState();
    Console.WriteLine($"Balance before save: {state.Balance}");  // 150

    // SaveChanges persists the events to the store.
    // Without this call, disposing the session discards all events.
    await session.SaveChanges();
    Console.WriteLine("Account created and events saved.");
}

// ── Load the account and make a withdrawal ──
// When we call BeginSessionFor again, BullOak:
// 1. Reads all stored events for this ID (AccountOpened + MoneyDeposited)
// 2. Creates a blank AccountState
// 3. Replays both events through the appliers to reconstruct the state
// This entire process is called "rehydration."
using (var session = await repo.BeginSessionFor(accountId))
{
    Console.WriteLine($"\nIs new account? {session.IsNewState}");  // False

    var state = session.GetCurrentState();
    Console.WriteLine($"Current balance: {state.Balance}");  // 150

    // Domain logic: check sufficient funds before withdrawing.
    // This is where your business rules live — in your application code,
    // NOT inside the applier. The applier just records the state change.
    if (state.Balance >= 30m)
    {
        session.AddEvent(new MoneyWithdrawn(30m));
    }

    await session.SaveChanges();

    state = session.GetCurrentState();
    Console.WriteLine($"Balance after withdrawal: {state.Balance}");  // 120
    Console.WriteLine($"Total transactions: {state.TransactionCount}");  // 3
}

// ── Load read-only — just to inspect state ──
using (var session = await repo.BeginSessionFor(accountId))
{
    var state = session.GetCurrentState();
    Console.WriteLine($"\nFinal balance: {state.Balance}");  // 120
    // Disposing without calling SaveChanges is perfectly fine.
    // This is the "read-only" pattern — load, inspect, dispose.
}
```

**Output:**
```
Is new account? True
Balance before save: 150
Account created and events saved.

Is new account? False
Current balance: 150
Balance after withdrawal: 120
Total transactions: 3

Final balance: 120
```

---

## Core Concepts

### Events

Events are plain C# objects (classes or records) that describe **something that happened** in your domain. They are the fundamental building block of event sourcing.

**Key rules for events:**

1. **Use past tense** — events describe things that already happened: `OrderPlaced`, not `PlaceOrder`
2. **Immutable** — once stored, events are never modified. Use records or classes with read-only semantics.
3. **Self-descriptive** — each event should carry all the data needed to understand what happened, without needing external context
4. **Domain-focused** — events describe business facts, not technical operations. `MoneyDeposited` not `BalanceFieldUpdated`.

```csharp
// Using C# records (recommended — concise, immutable by default, value equality)
public record OrderPlaced(string OrderId, string CustomerId, DateTime PlacedAt);
public record ItemAdded(string ProductId, int Quantity, decimal UnitPrice);
public record OrderCompleted(DateTime CompletedAt);

// Using classes (also works — useful when you need mutable properties for BullOak's
// interface-event creation pattern via AddEvent<T>(Action<T>))
public class OrderPlaced
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public DateTime PlacedAt { get; set; }
}
```

**Events are stored in streams.** A stream is a named, ordered sequence of events that represents one aggregate instance. Think of it as a partition key:

- `order-abc123` — all events for order abc123
- `account-jane` — all events for Jane's account
- `cinema-odeon-1` — all events for cinema Odeon screen 1

Streams are **append-only** — you can only add events to the end, never modify or delete individual events.

**Inside BullOak**, events flow through two wrapper types:

- **`ItemWithType`** — a struct that pairs an event `object` with its runtime `Type`. This is necessary because BullOak handles events polymorphically (as `object`) but needs the runtime type for applier dispatch. When you call `session.AddEvent(new MoneyDeposited(50m))`, BullOak wraps it in `new ItemWithType(event, typeof(MoneyDeposited))`.
- **`StoredEvent`** — a struct for persisted events: the event object, its `Type`, and its `EventIndex` (position in the stream, starting at 0). This is what the rehydration pipeline works with.

### State

State is the **current snapshot** of an aggregate, derived by replaying all events in its stream. You define what state looks like as a plain C# type; BullOak handles the reconstruction.

BullOak supports two approaches, each with different tradeoffs:

**Class-based state** — simple, familiar, mutable:

```csharp
public class OrderState
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public List<LineItem> Items { get; set; } = new();
    public decimal Total { get; set; }
    public bool IsCompleted { get; set; }
}
```

Pros: Easy to understand, works like any C# class, no magic.
Cons: Nothing prevents your application code from accidentally doing `state.Total = 999` outside of an applier. You rely on developer discipline.

**Interface-based state** — recommended for production use:

```csharp
public interface IOrderState
{
    string OrderId { get; set; }
    string CustomerId { get; set; }
    List<LineItem> Items { get; set; }
    decimal Total { get; set; }
    bool IsCompleted { get; set; }
}
```

Pros: BullOak generates a concrete class at runtime (via `System.Reflection.Emit` IL emission) where **property setters are locked by default**. They only work when BullOak is actively applying events through an applier. Any attempt to set a property outside of event application throws an exception: `"You can only edit this item during reconstitution"`. This enforces the invariant that all state changes flow through events.

Cons: Slightly more complex to understand at first. The generated type is not visible in your code, so debugging requires understanding the emission mechanism (see [Dynamic Type Emission](#dynamic-type-emission)).

**Interface inheritance** is fully supported. BullOak's `InterfaceFlattener` walks the entire interface hierarchy to collect all properties:

```csharp
public interface IHasName
{
    string Name { get; set; }
}

public interface IHasBalance
{
    decimal Balance { get; set; }
}

// The generated class will have both Name and Balance properties
public interface IAccountState : IHasName, IHasBalance
{
    bool IsClosed { get; set; }
}
```

### Appliers (Event Handlers)

Appliers are the core of BullOak's event sourcing model. They define **how each event type transforms state**. Mathematically, an applier is a folding function: it takes the current state and an event, and returns the new state.

```
Given events [e₁, e₂, e₃] and an initial empty state s₀:

  s₁ = apply(s₀, e₁)    // AccountOpened: balance becomes 100
  s₂ = apply(s₁, e₂)    // MoneyDeposited: balance becomes 150
  s₃ = apply(s₂, e₃)    // MoneyWithdrawn: balance becomes 120

  Final state = s₃
```

BullOak calls appliers in two situations:
1. **During rehydration** — when loading an aggregate, all stored events are replayed through appliers to reconstruct the current state
2. **During `AddEvent()`** — when you add a new event to a session, the applier is called immediately to update the in-memory state (eager application)

There are **four ways** to write appliers, from most common to least:

**1. One class per event type using `BaseApplyEvents<TState, TEvent>`** (recommended):

This is the most common and clearest pattern. Each applier class handles exactly one event type. `BaseApplyEvents` is an abstract convenience class that implements the `IApplyEvent<TState, TEvent>` interface and takes care of the boilerplate.

```csharp
public class OrderPlacedApplier : BaseApplyEvents<OrderState, OrderPlaced>
{
    public override OrderState Apply(OrderState state, OrderPlaced @event)
    {
        state.OrderId = @event.OrderId;
        state.CustomerId = @event.CustomerId;
        return state;
    }
}
```

**2. One class implementing `IApplyEvent<TState, TEvent>` for multiple event types:**

Useful when several events are closely related and you want to keep their logic together.

```csharp
public class OrderApplier :
    IApplyEvent<OrderState, OrderPlaced>,
    IApplyEvent<OrderState, ItemAdded>,
    IApplyEvent<OrderState, OrderCompleted>
{
    public OrderState Apply(OrderState state, OrderPlaced @event)
    {
        state.OrderId = @event.OrderId;
        state.CustomerId = @event.CustomerId;
        return state;
    }

    public OrderState Apply(OrderState state, ItemAdded @event)
    {
        state.Items.Add(new LineItem(@event.ProductId, @event.Quantity, @event.UnitPrice));
        state.Total += @event.Quantity * @event.UnitPrice;
        return state;
    }

    public OrderState Apply(OrderState state, OrderCompleted @event)
    {
        state.IsCompleted = true;
        return state;
    }
}
```

**3. Switch-based applier using `IApplyEvents<TState>`:**

This interface gives you full control over dispatch. You implement `CanApplyEvent(Type)` to declare which events you handle, and `Apply(TState, object)` to dispatch them. This is the most flexible but most verbose pattern.

```csharp
public class OrderReconstitutor : IApplyEvents<OrderState>
{
    public bool CanApplyEvent(Type eventType)
        => eventType == typeof(OrderPlaced)
        || eventType == typeof(ItemAdded)
        || eventType == typeof(OrderCompleted);

    public OrderState Apply(OrderState state, object @event)
    {
        return @event switch
        {
            OrderPlaced e => ApplyPlaced(state, e),
            ItemAdded e => ApplyItemAdded(state, e),
            OrderCompleted e => ApplyCompleted(state, e),
            _ => throw new InvalidOperationException($"Unknown event type: {@event.GetType().Name}")
        };
    }

    private OrderState ApplyPlaced(OrderState state, OrderPlaced e) { /* ... */ return state; }
    private OrderState ApplyItemAdded(OrderState state, ItemAdded e) { /* ... */ return state; }
    private OrderState ApplyCompleted(OrderState state, OrderCompleted e) { /* ... */ return state; }
}
```

**4. Lambda appliers using `FuncEventApplier<TState, TEvent>`:**

For quick prototyping or tests. `FuncEventApplier` has an implicit conversion from `Func<TState, TEvent, TState>`, so you can pass a lambda directly.

```csharp
FuncEventApplier<OrderState, OrderPlaced> applier =
    (state, @event) => { state.OrderId = @event.OrderId; return state; };
```

**Assembly scanning:** BullOak discovers appliers automatically. When you call `.WithAnyAppliersFrom(Assembly.GetExecutingAssembly())` in the configuration, BullOak scans the assembly for all public and internal classes that implement `IApplyEvent<,>`, `IApplyEvents<>`, or `BaseApplyEvents<,>`. It instantiates each one via `Activator.CreateInstance()` (they must have a public parameterless constructor). If you need dependency injection, use `.WithAnyAppliersFromInstances(preCreatedAppliers)` instead.

### Configuration

BullOak uses a **fluent builder pattern** that enforces the correct setup order at compile time. This is a deliberate design choice: each step in the configuration chain returns a **different interface**, so the C# compiler ensures you call methods in the right sequence. You physically cannot skip a step or call things out of order.

```csharp
var configuration = Configuration.Begin()       // returns IConfigureEventCollectionType
    .WithDefaultCollection()                    // returns IConfigureStateFactory
    .WithDefaultStateFactory()                  // returns IConfigureThreadSafety
    .NeverUseThreadSafe()                       // returns IConfigureEventPublisher
    .WithNoEventPublisher()                     // returns IManuallyConfigureEventAppliers
    .WithAnyAppliersFrom(Assembly.GetExecutingAssembly())  // returns IManuallyConfigureEventAppliers (chainable)
    .AndNoMoreAppliers()                        // returns IConfigureUpconverter
    .WithNoUpconverters()                       // returns IBuildConfiguration
    .Build();                                   // returns IHoldAllConfiguration
```

The final result (`IHoldAllConfiguration`) is a runtime configuration bag that holds all the wired-up components: the state factory, event applier, state rehydrator, event publisher, upconverters, interceptors, thread-safety settings, and the collection type for new events. You pass this to a repository constructor.

All the configuration steps are implemented by a single internal class (`ConfigurationOwner`) that carries state through the builder chain. The separate interfaces are purely for compile-time safety.

**Interceptors are special:** `.WithInterceptor(interceptor)` can be called at **any step** in the chain because all step interfaces inherit from `IConfigureBullOak`, which exposes the `AddInterceptor()` method.

### Sessions (Unit of Work)

A **session** (`IManageSessionOf<TState>`) is BullOak's unit of work. It represents a single interaction with one aggregate: load the current state, make domain decisions, record new events, and save. It is analogous to a database transaction.

Here is the complete session lifecycle:

```csharp
// 1. BEGIN — Load the aggregate
//    BullOak reads all stored events, runs them through upconverters,
//    then replays them through appliers to reconstruct the current state.
//    This entire process is called "rehydration."
using (var session = await repo.BeginSessionFor("order-123"))
{
    // 2. READ — Inspect the current state
    //    GetCurrentState() returns the reconstituted state object.
    //    For interface-based state, this object is READ-ONLY at this point.
    var state = session.GetCurrentState();

    // 3. DECIDE — Apply domain logic
    //    Your business rules live HERE, in your application code.
    //    The session gives you the current state to make decisions with.
    if (!state.IsCompleted && state.Items.Count > 0)
    {
        // 4. RECORD — Add new events
        //    AddEvent does TWO things:
        //    a) Records the event in the session's new-events collection
        //    b) IMMEDIATELY applies it to the in-memory state via the applier
        //    This means GetCurrentState() reflects the new event right away.
        session.AddEvent(new OrderCompleted(DateTime.UtcNow));

        // You can check the updated state immediately:
        var updatedState = session.GetCurrentState();
        // updatedState.IsCompleted is now true
    }

    // 5. SAVE — Persist to the event store
    //    SaveChanges() does the following in order (for AtLeastOnce delivery):
    //    a) Validates the state (if a validator is configured)
    //    b) Publishes events to the message bus (if a publisher is configured)
    //    c) Calls interceptors (BeforeSave)
    //    d) Persists events to the underlying store
    //    e) Calls interceptors (AfterSave)
    //    f) Clears the new-events collection
    await session.SaveChanges();
}
// 6. DISPOSE — End the session
//    If you DID call SaveChanges: events are persisted, all good.
//    If you DID NOT call SaveChanges: all new events are discarded.
//    This is the explicit "rollback" pattern — dispose without save = discard.
```

**The `IManageSessionOf<TState>` interface** exposes these members:

| Member | Description |
|--------|-------------|
| `bool IsNewState` | `true` if no stored events were found (brand-new aggregate). `false` if events were loaded from the store. Use this to guard against illegal operations on new vs. existing aggregates. |
| `TState GetCurrentState()` | Returns the live in-memory state. Always reflects all events, including unsaved ones added via `AddEvent()`. |
| `void AddEvent(object @event)` | Records an event and immediately applies it to the state. |
| `void AddEvent<TEvent>(Action<TEvent> init)` | Creates an instance of `TEvent` (supports interfaces — BullOak generates a concrete type), temporarily unlocks it for writing, calls your action to initialize it, then locks it and records it. |
| `void AddEvents(object[] events)` | Records multiple events at once, applying each in order. |
| `Task<int> SaveChanges(...)` | Validates, publishes, saves. Can be called multiple times. Returns the number of events saved. |
| `void Dispose()` | Ends the session. Unsaved events are discarded. |

### Repository

The repository is the entry point for working with aggregates. It creates sessions and manages the connection to the underlying event store. BullOak defines the contract via the `IStartSessions<TEntitySelector, TState>` interface:

```csharp
public interface IStartSessions<TEntitySelector, TState>
{
    // Load an aggregate and return a session for interacting with it.
    // selector: the aggregate/stream identifier (e.g., "order-123")
    // throwIfNotExists: if true, throws StreamNotFoundException when the stream is empty.
    //                   if false, returns a session with IsNewState = true.
    // appliesAt: optional point-in-time filter — only events at or before this timestamp
    //            are replayed. Enables temporal queries ("what was the state on March 10th?").
    Task<IManageSessionOf<TState>> BeginSessionFor(
        TEntitySelector selector,
        bool throwIfNotExists = false,
        DateTime? appliesAt = null);

    // Delete an aggregate's entire event stream.
    Task Delete(TEntitySelector selector);

    // Check if an aggregate exists (has at least one stored event).
    Task<bool> Contains(TEntitySelector selector);
}
```

**BullOak includes `InMemoryEventSourcedRepository<TId, TState>`** — a complete, working implementation backed by `ConcurrentDictionary<TId, List<(StoredEvent, DateTime)>>`. Each event is stored alongside a `DateTime` timestamp for point-in-time query support.

```csharp
// Create a repository. TId can be any type (string, int, Guid, a custom ID class).
var repo = new InMemoryEventSourcedRepository<string, OrderState>(configuration);

// Or with a custom validator:
var repo = new InMemoryEventSourcedRepository<string, OrderState>(
    new OrderValidator(),
    configuration);

// Start a session (creates a new aggregate if it doesn't exist)
using var session = await repo.BeginSessionFor("order-123");

// Start a session (throws StreamNotFoundException if aggregate doesn't exist)
using var session = await repo.BeginSessionFor("order-123", throwIfNotExists: true);

// Check if an aggregate exists
bool exists = await repo.Contains("order-123");

// Delete an aggregate's entire event stream
await repo.Delete("order-123");

// Direct access to the underlying store (useful for test setup):
repo["order-123"] = new List<(StoredEvent, DateTime)> { /* pre-populate events */ };
```

For production use with external stores (EventStoreDB, SQL Server, MongoDB, etc.), you extend `BaseEventSourcedSession<TState>` — see [Integrating with External Event Stores](#integrating-with-external-event-stores).

---

## Architecture Overview

### High-Level Data Flow

```
                         ┌─────────────────────────────────────────┐
                         │            Your Application             │
                         │                                         │
                         │   session = repo.BeginSessionFor(id)    │
                         │   state = session.GetCurrentState()     │
                         │   session.AddEvent(new OrderPlaced())   │
                         │   session.SaveChanges()                 │
                         └────────────────┬────────────────────────┘
                                          │
                    ┌─────────────────────┼─────────────────────┐
                    │                     │                     │
                    ▼                     ▼                     ▼
          ┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐
          │   Repository    │   │    Session       │   │  Configuration  │
          │                 │   │  (Unit of Work)  │   │                 │
          │ BeginSessionFor │──▶│ GetCurrentState  │◀──│ StateFactory    │
          │ Contains        │   │ AddEvent         │   │ EventApplier    │
          │ Delete          │   │ SaveChanges      │   │ StateRehydrator │
          └────────┬────────┘   └────────┬─────────┘   │ EventPublisher  │
                   │                     │              │ Upconverters    │
                   │              ┌──────┴──────┐       │ Interceptors    │
                   │              │             │       └─────────────────┘
                   ▼              ▼             ▼
          ┌─────────────┐  ┌──────────┐  ┌──────────┐
          │ Event Store │  │ Appliers │  │Publisher │
          │ (storage)   │  │ (state   │  │ (message │
          │             │  │  folding) │  │  bus)    │
          └─────────────┘  └──────────┘  └──────────┘
```

**Data flow when loading an aggregate (rehydration):**
```
Event Store
    │
    ▼
StoredEvent[] (raw events from storage)
    │
    ▼
Upconverters (transform old event schemas → current schemas, if registered)
    │
    ▼
StateFactory creates blank initial state (EmittedTypeFactory for interfaces, Activator for classes)
    │
    ▼
EventApplier.Apply() replays each event through the matching applier
    │
    ▼
Reconstituted State (returned via session.GetCurrentState())
```

**Data flow when saving:**
```
session.AddEvent(event)
    │
    ├─→ Event recorded in new-events collection
    └─→ Applier immediately updates in-memory state (eager application)

session.SaveChanges()
    │
    ├─→ Validator checks state (throws if invalid)
    ├─→ Publisher sends events to message bus
    ├─→ Interceptors: BeforeSave hooks
    ├─→ Store-specific SaveChanges (persists events)
    └─→ Interceptors: AfterSave hooks
```

### Class Hierarchy

```
Session classes (you extend these for custom stores):

  IManageSessionOf<TState>                   ← public interface (what your app uses)
      │
      ▼
  BaseRepoSession<TState>                    ← abstract: publish/save/intercept logic
      │
      ▼
  BaseEventSourcedSession<TState>            ← abstract: adds LoadFromEvents() for rehydration
      │
      ▼
  InMemoryEventStoreSession<TState, TId>     ← concrete: saves to ConcurrentDictionary


Repository classes:

  IStartSessions<TId, TState>               ← public interface
      │
      ▼
  InMemoryEventSourcedRepository<TId, TState> ← concrete: creates InMemoryEventStoreSessions


Applier interfaces:

  IApplyEvent<TState, TEvent>               ← handles one specific event type
      │
      ▼
  BaseApplyEvents<TState, TEvent>            ← abstract convenience base class

  IApplyEvents<TState>                       ← handles multiple event types (switch-based)

  FuncEventApplier<TState, TEvent>           ← wraps a lambda (implicit conversion from Func)
```

---

## Step-by-Step Tutorial: Bank Account

This section walks through building a complete bank account aggregate from scratch, explaining every piece and why it exists.

### 1. Define Events

Events describe what happened in your domain. Think of them as entries in a ledger — each one is a fact that cannot be changed after the fact.

```csharp
namespace MyApp.Events;

// Naming convention: use past tense, domain language.
// "AccountOpened" not "OpenAccount" or "AccountCreatedEvent" or "CreateAccountCommand".
// The "Event" suffix is optional — some teams prefer it for clarity.

public record AccountOpened(string AccountId, string OwnerName, decimal InitialDeposit);

public record MoneyDeposited(decimal Amount, string Description);

public record MoneyWithdrawn(decimal Amount, string Description);

public record AccountClosed(string Reason);
```

**Why records?** C# records give you immutability, value equality, and concise syntax. However, BullOak works with any class — records are not required.

**What should go IN an event?** Everything needed to understand what happened: the account ID, the amount, a description. **What should NOT go in an event?** Derived data (like the new balance — that's computed by the applier) or data that changes over time (like the user's current email address).

### 2. Define State

State is the "current view" of your aggregate. It is **derived** from events, never stored directly. Think of it as a materialized view that BullOak rebuilds for you every time you load the aggregate.

```csharp
namespace MyApp.State;

public class AccountState
{
    public string AccountId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public decimal Balance { get; set; }
    public bool IsClosed { get; set; }
    public List<string> TransactionLog { get; set; } = new();
}
```

**Why a `List<string>` for TransactionLog?** This demonstrates that state can contain collections that grow as events are applied. Each applier appends a human-readable entry. In production, you might use a more structured type.

**Default values matter.** When BullOak creates a blank state (for a new aggregate or at the start of rehydration), it uses `Activator.CreateInstance()` or the `EmittedTypeFactory`. Properties will have their default values (`""`, `0m`, `false`, etc.) or whatever you initialize them to.

### 3. Write Appliers

Each applier tells BullOak how one event type changes the state. The applier receives the current state and the event, mutates the state, and returns it.

**Important:** Appliers should be pure transformation functions. They should NOT contain business logic decisions (like "can this account be closed?"). Business decisions belong in your application code, before you call `AddEvent()`. The applier just records the resulting state change.

```csharp
namespace MyApp.Appliers;

using BullOak.Repositories.Appliers;
using MyApp.Events;
using MyApp.State;

public class AccountOpenedApplier : BaseApplyEvents<AccountState, AccountOpened>
{
    public override AccountState Apply(AccountState state, AccountOpened @event)
    {
        state.AccountId = @event.AccountId;
        state.OwnerName = @event.OwnerName;
        state.Balance = @event.InitialDeposit;
        state.TransactionLog.Add($"Account opened with deposit of {@event.InitialDeposit:C}");
        return state;
    }
}

public class MoneyDepositedApplier : BaseApplyEvents<AccountState, MoneyDeposited>
{
    public override AccountState Apply(AccountState state, MoneyDeposited @event)
    {
        state.Balance += @event.Amount;
        state.TransactionLog.Add($"Deposited {@event.Amount:C} — {@event.Description}");
        return state;
    }
}

public class MoneyWithdrawnApplier : BaseApplyEvents<AccountState, MoneyWithdrawn>
{
    public override AccountState Apply(AccountState state, MoneyWithdrawn @event)
    {
        state.Balance -= @event.Amount;
        state.TransactionLog.Add($"Withdrew {@event.Amount:C} — {@event.Description}");
        return state;
    }
}

public class AccountClosedApplier : BaseApplyEvents<AccountState, AccountClosed>
{
    public override AccountState Apply(AccountState state, AccountClosed @event)
    {
        state.IsClosed = true;
        state.TransactionLog.Add($"Account closed — {@event.Reason}");
        return state;
    }
}
```

**Why does `Apply()` return `state`?** This supports both mutation-based and replacement-based patterns. For class-based state, you typically mutate and return the same object. For immutable state (records/structs), you would create and return a new instance.

### 4. Configure BullOak

```csharp
using System.Reflection;
using BullOak.Repositories;

var configuration = Configuration.Begin()
    .WithDefaultCollection()       // BullOak's SpinLock-backed linked list for new events
    .WithDefaultStateFactory()     // EmittedTypeFactory: handles both classes and interfaces
    .NeverUseThreadSafe()          // single-threaded access per session (faster)
    .WithNoEventPublisher()        // no external message bus (for now)
    .WithAnyAppliersFrom(Assembly.GetExecutingAssembly())  // scans and finds all 4 appliers above
    .AndNoMoreAppliers()
    .WithNoUpconverters()          // no legacy event migration needed (yet)
    .Build();
```

### 5. Create the Repository

```csharp
using BullOak.Repositories.InMemory;

// TId = string (the stream/aggregate identifier type)
// TState = AccountState (what we're reconstructing from events)
var repo = new InMemoryEventSourcedRepository<string, AccountState>(configuration);
```

### 6. Use Sessions to Read and Write

```csharp
var accountId = "ACC-001";

// ── Create a new account ──
using (var session = await repo.BeginSessionFor(accountId))
{
    // session.IsNewState == true (no events exist for this ID)
    session.AddEvent(new AccountOpened(accountId, "Alice Smith", 500m));
    await session.SaveChanges();
}

// ── Deposit money ──
using (var session = await repo.BeginSessionFor(accountId))
{
    // BullOak replayed the AccountOpened event → state.Balance = 500
    session.AddEvent(new MoneyDeposited(200m, "Salary"));
    await session.SaveChanges();
}

// ── Withdraw with domain logic check ──
using (var session = await repo.BeginSessionFor(accountId))
{
    var state = session.GetCurrentState();
    // state.Balance is 700 (500 + 200), reconstructed by replaying 2 stored events

    decimal withdrawalAmount = 150m;

    // Business logic check: sufficient funds AND account not closed
    if (state.Balance >= withdrawalAmount && !state.IsClosed)
    {
        session.AddEvent(new MoneyWithdrawn(withdrawalAmount, "ATM withdrawal"));
        await session.SaveChanges();
    }
}

// ── Read the final state ──
using (var session = await repo.BeginSessionFor(accountId))
{
    var state = session.GetCurrentState();
    Console.WriteLine($"Account: {state.AccountId}");        // ACC-001
    Console.WriteLine($"Owner: {state.OwnerName}");          // Alice Smith
    Console.WriteLine($"Balance: {state.Balance:C}");        // $550.00
    Console.WriteLine($"Transactions:");
    foreach (var entry in state.TransactionLog)
        Console.WriteLine($"  - {entry}");
    // - Account opened with deposit of $500.00
    // - Deposited $200.00 — Salary
    // - Withdrew $150.00 — ATM withdrawal
}
```

---

## A More Realistic Example: Cinema Reservation System

The end-to-end tests in this repository demonstrate BullOak with a more complex domain: a cinema with viewings (screenings) and seat reservations. This example shows aggregate roots, child entities, and interface-based state.

**Domain model:**

```
Cinema (aggregate root)
├── CinemaId { Name }
├── NumberOfSeats
└── Viewings
    └── Viewing (child aggregate)
        ├── ViewingId
        └── Seats[]
            ├── IsReserved
            └── SeatNumber
```

**Events:**

```csharp
public class CinemaCreated
{
    public CinemaId CinemaId { get; set; }
    public int Capacity { get; set; }
}

public class ViewingCreatedEvent
{
    public ViewingId ViewingId { get; set; }
    public Seats[] Seats { get; set; }  // array of seat objects, all unreserved
}

public class SeatReservedEvent
{
    public int SeatNumber { get; set; }
}
```

**Interface-based state (BullOak generates the concrete class):**

```csharp
public interface IViewingState
{
    ViewingId ViewingId { get; set; }
    Seats[] Seats { get; set; }
}
```

**Domain logic with invariant enforcement:**

```csharp
public static class ViewingAggregateRoot
{
    public static SeatReservedEvent ReserveSeat(IViewingState state, int seatNumber)
    {
        // Business rule: seat must not already be reserved
        var seat = state.Seats.First(s => s.SeatNumber == seatNumber);
        if (seat.IsReserved)
            throw new InvalidOperationException($"Seat {seatNumber} is already reserved");

        return new SeatReservedEvent { SeatNumber = seatNumber };
    }
}
```

**Usage:**

```csharp
var viewingRepo = new InMemoryEventSourcedRepository<ViewingId, IViewingState>(configuration);

// Create a viewing with 20 seats
using (var session = await viewingRepo.BeginSessionFor(viewingId))
{
    var seats = Enumerable.Range(1, 20)
        .Select(n => new Seats { SeatNumber = n, IsReserved = false })
        .ToArray();
    session.AddEvent(new ViewingCreatedEvent { ViewingId = viewingId, Seats = seats });
    await session.SaveChanges();
}

// Reserve a seat
using (var session = await viewingRepo.BeginSessionFor(viewingId))
{
    var state = session.GetCurrentState();
    var @event = ViewingAggregateRoot.ReserveSeat(state, seatNumber: 5);
    session.AddEvent(@event);
    await session.SaveChanges();
}
```

This pattern — aggregate root methods that take state and return events — keeps business logic testable and separate from the persistence mechanism.

---

## Advanced Features

### Interface-Based State (Dynamic Type Generation)

This is one of BullOak's most powerful features. When you define state as an **interface**, BullOak uses `System.Reflection.Emit` to generate a concrete class at runtime. The generated class has a crucial property: **setters are locked by default**. They only work when BullOak is actively applying events through an applier.

This means your application code can read state freely, but any attempt to bypass the event-driven flow and set properties directly will throw an exception. This is a compile-time-invisible but runtime-enforced safety net.

```csharp
// Define state as an interface
public interface IAccountState
{
    string AccountId { get; set; }
    decimal Balance { get; set; }
    bool IsClosed { get; set; }
}

// Write appliers against the interface — they work identically
public class AccountOpenedApplier : BaseApplyEvents<IAccountState, AccountOpened>
{
    public override IAccountState Apply(IAccountState state, AccountOpened @event)
    {
        // This works — BullOak temporarily sets canEdit = true during Apply()
        state.AccountId = @event.AccountId;
        state.Balance = @event.InitialDeposit;
        return state;
    }
}

// In your application code:
var repo = new InMemoryEventSourcedRepository<string, IAccountState>(configuration);

using (var session = await repo.BeginSessionFor("ACC-001"))
{
    var state = session.GetCurrentState();

    // Reading works fine — getters are always allowed
    Console.WriteLine(state.Balance);      // works
    Console.WriteLine(state.AccountId);    // works

    // Writing OUTSIDE of an applier throws an exception!
    // state.Balance = 999;
    // ↑ throws: "You can only edit this item during reconstitution"
    //
    // This forces all state changes to go through events, ensuring
    // your audit trail is complete and your state is always consistent.
}
```

**How it works step by step:**

1. BullOak's `EmittedTypeFactory` detects that `IAccountState` is an interface
2. It creates a new in-memory assembly using `System.Reflection.Emit.AssemblyBuilder`
3. It generates a class with:
   - A private backing field for each property
   - A getter that reads the backing field (always works)
   - A setter that checks a `canEdit` boolean flag — throws if `false`, sets the field if `true`
   - An implementation of `ICanSwitchBackAndToReadOnly` to control the flag
4. The class implements all interfaces in the inheritance chain (via `InterfaceFlattener`)
5. The factory is cached in a `ConcurrentDictionary` — generation happens only once per type

**Interface-based events** are also supported:

```csharp
public interface IMoneyDeposited
{
    decimal Amount { get; set; }
    string Description { get; set; }
}

// AddEvent<T>(Action<T>) creates a generated instance, unlocks it,
// calls your initializer, locks it, then records it:
session.AddEvent<IMoneyDeposited>(e =>
{
    e.Amount = 200m;
    e.Description = "Salary";
});
```

### Event Upconversion (Schema Evolution)

In any long-lived system, event schemas will evolve. Fields get renamed, events get split or merged, new data gets added. Upconverters handle this transparently: they transform old stored events into the current schema **at load time**, without rewriting the event store.

**When to use upconverters:**
- You renamed a field: `BuyerNameSet.Name` → `BuyerFullNameSet.FullName`
- You split an event: `BalanceUpdated` (with balance + timestamp) → separate `BalanceSet` and `BalanceTimestamped` events
- You merged fields: `FirstName` + `LastName` → `FullName`
- You changed a type: `string Amount` → `decimal Amount`

Your appliers only need to handle the **current** event schemas. Upconverters bridge the gap between old stored events and current appliers.

**One-to-one upconversion** (one old event becomes one new event):

```csharp
// Old event (stored in the database from months ago)
public class BuyerNameSet
{
    public string Title { get; set; }
    public string FirstName { get; set; }
    public string Surname { get; set; }
}

// New event (what your current appliers expect)
public class BuyerFullNameSet
{
    public string FullName { get; set; }
}

// Upconverter: transforms old → new at load time
// Implements IUpconvertEvent<TSource, TDestination>
public class BuyerNameUpconverter : IUpconvertEvent<BuyerNameSet, BuyerFullNameSet>
{
    public BuyerFullNameSet Upconvert(BuyerNameSet source)
        => new BuyerFullNameSet
        {
            FullName = $"{source.Title} {source.FirstName} {source.Surname}"
        };
}
```

**One-to-many upconversion** (one old event becomes multiple new events):

```csharp
// Old event: stored balance and timestamp together
public class BalanceUpdated
{
    public decimal Balance { get; set; }
    public DateTime UpdatedDate { get; set; }
}

// New design: separate concerns into two events
public class BalanceSet { public decimal Balance { get; set; } }
public class BalanceUpdateTimestamped { public DateTime UpdatedDate { get; set; } }

// Implements IUpconvertEvent<TSource> (no destination type — returns IEnumerable<object>)
public class BalanceUpconverter : IUpconvertEvent<BalanceUpdated>
{
    public IEnumerable<object> Upconvert(BalanceUpdated source)
    {
        yield return new BalanceSet { Balance = source.Balance };
        yield return new BalanceUpdateTimestamped { UpdatedDate = source.UpdatedDate };
    }
}
```

**Chaining**: Upconverters chain automatically. If event A upconverts to B, and B has its own upconverter to C, loading event A produces event C. Chains of arbitrary depth are supported. BullOak applies each upconverter recursively until no more upconverters match the result type.

**Compile-time validation**: At configuration time (`.AndNoMoreUpconverters()`), BullOak validates that no two upconverters share the same source event type. If they do, it throws `PreflightUpconverterConflictException` — this catches ambiguity early, not at runtime.

**Registration**:

```csharp
Configuration.Begin()
    // ... other steps ...
    .WithUpconvertersFrom(typeof(BuyerNameUpconverter))     // explicit type
    // or
    .WithUpconvertersFrom(Assembly.GetExecutingAssembly())  // scan assembly
    // or chain multiple:
    .WithUpconverter<BuyerNameUpconverter>()
    .WithUpconverter<BalanceUpconverter>()
    .AndNoMoreUpconverters()      // seals and compiles the upconverter list
    .Build();
```

When no upconverters are needed, use `.WithNoUpconverters()` which installs a `NullUpconverter` that passes all events through unchanged.

### Event Publishing

Publish events to an external message bus (RabbitMQ, Kafka, Azure Service Bus, etc.) as part of the save lifecycle. Events are published **in order, one at a time** (sequentially awaited), to preserve ordering guarantees.

```csharp
Configuration.Begin()
    // ... other steps ...

    // Async publisher with cancellation support (recommended for production)
    .WithEventPublisher(async (eventWithType, cancellationToken) =>
    {
        // eventWithType is an ItemWithType struct:
        //   eventWithType.instance → the event object (e.g., MoneyDeposited)
        //   eventWithType.type     → the runtime Type (e.g., typeof(MoneyDeposited))
        var json = JsonSerializer.Serialize(eventWithType.instance, eventWithType.type);
        await messageBus.PublishAsync(eventWithType.type.Name, json, cancellationToken);
    })

    // Or simpler async without cancellation:
    .WithEventPublisher(async (eventWithType) =>
    {
        await bus.PublishAsync(eventWithType.instance);
    })

    // Or synchronous:
    .WithEventPublisher((eventWithType) =>
    {
        Console.WriteLine($"Published: {eventWithType.type.Name}");
    })

    // ... rest of chain
```

When no publisher is needed, use `.WithNoEventPublisher()` which installs a no-op singleton.

### Event Interception (Middleware)

Interceptors provide four hooks into the event lifecycle, called for **each event** during `SaveChanges()`. Use them for logging, metrics, auditing, or any cross-cutting concern that should not live in your domain code.

```csharp
public class AuditInterceptor : IInterceptEvents
{
    // Called before the event is published to the message bus
    public void BeforePublish(object @event, Type eventType, object state, Type stateType)
        => Console.WriteLine($"[AUDIT] About to publish: {eventType.Name}");

    // Called after successful publication
    public void AfterPublish(object @event, Type eventType, object state, Type stateType)
        => Console.WriteLine($"[AUDIT] Published: {eventType.Name}");

    // Called before the event is persisted to the store
    public void BeforeSave(object @event, Type eventType, object state, Type stateType)
        => Console.WriteLine($"[AUDIT] About to save: {eventType.Name}");

    // Called after successful persistence
    public void AfterSave(object @event, Type eventType, object state, Type stateType)
        => Console.WriteLine($"[AUDIT] Saved: {eventType.Name}");
}

// Register at ANY point in the configuration chain:
Configuration.Begin()
    .WithDefaultCollection()
    .WithInterceptor(new AuditInterceptor())   // can be called at any step
    .WithDefaultStateFactory()
    // ... rest of chain
```

Multiple interceptors can be registered. They are called in registration order. The hook execution order depends on the delivery guarantee — see [Delivery Guarantees](#delivery-guarantees).

### State Validation (Invariants)

Enforce business rules (invariants) before events are persisted. Validation runs at the **start of `SaveChanges()`** — before any publishing or persistence. If validation fails, `SaveChanges()` throws an `AggregateException` containing one or more `BusinessException` instances, and **no events are published or stored**.

```csharp
public class AccountValidator : IValidateState<AccountState>
{
    public ValidationResults Validate(AccountState state)
    {
        var errors = new List<BasicValidationError>();

        if (state.Balance < -1000m)
            errors.Add("Account overdraft limit exceeded (max: -$1,000)");

        if (string.IsNullOrWhiteSpace(state.OwnerName))
            errors.Add("Account must have an owner name");

        // BasicValidationError has an implicit conversion from string,
        // so you can pass strings directly.

        if (errors.Any())
            return ValidationResults.Errors(errors.ToArray());

        return ValidationResults.Success();
    }
}

// Pass the validator to the repository constructor:
var repo = new InMemoryEventSourcedRepository<string, AccountState>(
    new AccountValidator(),
    configuration);

// Usage — if the withdrawal would exceed the overdraft limit:
using (var session = await repo.BeginSessionFor(accountId))
{
    session.AddEvent(new MoneyWithdrawn(99999m, "Oops"));

    try
    {
        await session.SaveChanges();
    }
    catch (AggregateException ex)
    {
        // ex.InnerExceptions contains BusinessException instances
        // Each BusinessException wraps an IValidationError
        foreach (var inner in ex.InnerExceptions)
            Console.WriteLine(inner.Message);  // "Account overdraft limit exceeded"
    }
}
```

When no validator is configured, BullOak uses `AlwaysPassValidator<TState>` which always returns `ValidationResults.Success()`.

### Point-in-Time Queries

The in-memory repository supports loading state **as it was at a specific moment in time**. This works because each event is stored alongside a `DateTime` timestamp. When you provide the `appliesAt` parameter, only events with a timestamp at or before that moment are replayed.

```csharp
// What was the account balance yesterday at 3 PM?
var pointInTime = new DateTime(2026, 3, 22, 15, 0, 0);

using var session = await repo.BeginSessionFor(accountId, false, pointInTime);
var historicalState = session.GetCurrentState();
Console.WriteLine($"Balance at {pointInTime}: {historicalState.Balance}");
```

This is one of the key advantages of event sourcing — temporal queries are trivial because you have the complete event history. In a traditional CRUD system, you would need a separate audit log or temporal tables to achieve this.

### Optimistic Concurrency

BullOak detects when two sessions try to write to the same stream concurrently, preventing lost updates. This is essential in any multi-user or multi-threaded system.

**How it works in the InMemory implementation:**

1. When a session is created (`BeginSessionFor`), it records `initialVersion = stream.Count`
2. When `SaveChanges()` is called, it checks: `stream.Count == initialVersion`?
3. If yes: save proceeds, `initialVersion` is updated
4. If no (another session wrote in between): `ConcurrencyException` is thrown

```csharp
// Scenario: two sessions load the same account simultaneously

using var sessionA = await repo.BeginSessionFor(accountId);
// sessionA records initialVersion = 3 (stream has 3 events)

using var sessionB = await repo.BeginSessionFor(accountId);
// sessionB also records initialVersion = 3

// Session A saves first — stream now has 4 events
sessionA.AddEvent(new MoneyDeposited(100m, "From A"));
await sessionA.SaveChanges();  // succeeds: stream.Count (3) == initialVersion (3)

// Session B tries to save — but stream has grown since it loaded
sessionB.AddEvent(new MoneyWithdrawn(50m, "From B"));
try
{
    await sessionB.SaveChanges();  // FAILS: stream.Count (4) != initialVersion (3)
}
catch (ConcurrencyException)
{
    // Handle the conflict: typically reload the aggregate and retry
    Console.WriteLine("Concurrent write detected — please retry");
}
```

External event stores (EventStoreDB, SQL, etc.) implement this differently — typically using stream revision numbers or ETags — but the concept is the same.

### Delivery Guarantees

`SaveChanges` accepts a `DeliveryTargetGuarntee` parameter (note: the typo in `Guarntee` is intentional — it matches the codebase enum name) that controls whether events are published **before or after** they are persisted:

```csharp
// AtLeastOnce (default): publish first, then save.
// If save fails after publish, events may have been published but not saved.
// On retry, they'll be published again → "at least once" delivery.
await session.SaveChanges(DeliveryTargetGuarntee.AtLeastOnce);

// AtMostOnce: save first, then publish.
// If publish fails after save, events are saved but never published.
// They won't be retried → "at most once" delivery.
await session.SaveChanges(DeliveryTargetGuarntee.AtMostOnce);
```

**Complete hook execution order per event:**

| Guarantee | Order |
|-----------|-------|
| `AtLeastOnce` (default) | BeforePublish → Publish → AfterPublish → BeforeSave → **Save** → AfterSave |
| `AtMostOnce` | BeforeSave → **Save** → AfterSave → BeforePublish → Publish → AfterPublish |

---

## Configuration Reference

### Fluent Builder Chain

The configuration builder enforces this exact sequence. Each step returns a different interface, enforced by the C# compiler:

```
Configuration.Begin()                          → IConfigureEventCollectionType
  .WithDefaultCollection()                     → IConfigureStateFactory
  .WithDefaultStateFactory()                   → IConfigureThreadSafety
  .NeverUseThreadSafe()                        → IConfigureEventPublisher
  .WithNoEventPublisher()                      → IManuallyConfigureEventAppliers
  .WithAnyAppliersFrom(assembly)               → IManuallyConfigureEventAppliers (chainable)
  .AndNoMoreAppliers()                         → IConfigureUpconverter
  .WithNoUpconverters()                        → IBuildConfiguration
  .Build()                                     → IHoldAllConfiguration
```

`.WithInterceptor(interceptor)` can be called at **any step** (all steps inherit `IConfigureBullOak`).

### Configuration Options

| Step | Options | Description |
|------|---------|-------------|
| **Collection** | `WithDefaultCollection()` | BullOak's custom `SpinLock`-backed singly-linked list for new events. O(1) append, optimized for small collections (outperforms `List<T>` for up to ~12 items in thread-safe mode). |
| **State Factory** | `WithDefaultStateFactory()` | `EmittedTypeFactory` — handles interfaces (generates classes via IL emission) and concrete classes (uses optimized `DynamicMethod` constructor call). This is the recommended option. |
| | `UseActivator()` | Uses `Activator.CreateInstance` — works with concrete classes only. Does NOT support interface-based state. |
| | `With(Func<Type, Func<object>>)` | Custom factory / DI container integration. The outer function receives a `Type` and returns a factory function for that type. |
| **Thread Safety** | `NeverUseThreadSafe()` | No locking on the new-events collection (faster, for single-threaded sessions — the common case) |
| | `AlwaysUseThreadSafe()` | `SpinLock` protection on the new-events collection. Use if multiple threads might add events to the same session concurrently. |
| **Publisher** | `WithNoEventPublisher()` | Installs a no-op singleton publisher |
| | `WithEventPublisher(Action<ItemWithType>)` | Synchronous publisher |
| | `WithEventPublisher(Func<ItemWithType, Task>)` | Async publisher |
| | `WithEventPublisher(Func<ItemWithType, CancellationToken, Task>)` | Async publisher with cancellation support (recommended for production) |
| **Appliers** | `WithAnyAppliersFrom(Assembly)` | Scan an assembly for all classes implementing `IApplyEvent<,>`, `IApplyEvents<>`, or `BaseApplyEvents<,>`. Instantiates via `Activator.CreateInstance` (must have parameterless constructor). |
| | `WithAnyAppliersFromInstances(IEnumerable<object>)` | Pre-instantiated appliers. Use this for dependency injection — create applier instances yourself and pass them in. |
| | `AndNoMoreAppliers()` | Seal the applier list and compile the applier lookup cache |
| **Upconverters** | `WithNoUpconverters()` | Installs `NullUpconverter` — passes all events through unchanged |
| | `WithUpconvertersFrom(Assembly)` | Scan assembly for upconverter classes |
| | `WithUpconvertersFrom(IEnumerable<Type>)` | Explicit type list |
| | `WithUpconverter<T>()` | Single upconverter type (chainable — call multiple times) |
| | `AndNoMoreUpconverters()` | Seal, validate (no duplicate source types), and compile upconverters |

---

## Integrating with External Event Stores

BullOak's in-memory repository is great for testing and prototyping, but for production you need a real event store (EventStoreDB, SQL Server, MongoDB, Cosmos DB, etc.). BullOak provides abstract base classes that handle all the rehydration, publishing, and interception logic — you only need to implement two things: **loading events** and **saving events**.

**Extend `BaseEventSourcedSession<TState>`:**

```csharp
public class SqlEventStoreSession<TState> : BaseEventSourcedSession<TState>
{
    private readonly SqlConnection _connection;
    private readonly string _streamId;
    private long _expectedVersion;

    public SqlEventStoreSession(
        IHoldAllConfiguration configuration,
        SqlConnection connection,
        string streamId)
        : base(configuration)
    {
        _connection = connection;
        _streamId = streamId;
    }

    /// <summary>
    /// Call this after construction to load events and reconstitute state.
    /// LoadFromEvents is inherited from BaseEventSourcedSession — it handles
    /// upconversion, state factory, and applier dispatch automatically.
    /// </summary>
    public async Task Initialize()
    {
        var storedEvents = await LoadEventsFromSql(_streamId);
        _expectedVersion = storedEvents.Length;
        LoadFromEvents(storedEvents);  // inherited method — handles everything
    }

    /// <summary>
    /// Called by the base class during SaveChanges().
    /// This is where you persist new events to your store.
    /// </summary>
    protected override async Task<int> SaveChanges(
        ItemWithType[] newEvents,
        TState currentState,
        CancellationToken cancellationToken)
    {
        // Implement optimistic concurrency check
        var currentVersion = await GetStreamVersion(_streamId);
        if (currentVersion != _expectedVersion)
            throw new ConcurrencyException($"Stream {_streamId} was modified concurrently");

        // Serialize and store each event
        foreach (var evt in newEvents)
        {
            var json = JsonSerializer.Serialize(evt.instance, evt.type);
            await InsertEventToSql(_streamId, evt.type.Name, json, cancellationToken);
        }

        _expectedVersion += newEvents.Length;
        return newEvents.Length;
    }

    private async Task<StoredEvent[]> LoadEventsFromSql(string streamId) { /* ... */ }
    private async Task<long> GetStreamVersion(string streamId) { /* ... */ }
    private async Task InsertEventToSql(string streamId, string type, string json, CancellationToken ct) { /* ... */ }
}
```

**Then build a repository around it:**

```csharp
public class SqlEventStoreRepository<TState> : IStartSessions<string, TState>
{
    private readonly IHoldAllConfiguration _config;
    private readonly Func<SqlConnection> _connectionFactory;

    public SqlEventStoreRepository(IHoldAllConfiguration config, Func<SqlConnection> connectionFactory)
    {
        _config = config;
        _connectionFactory = connectionFactory;
    }

    public async Task<IManageSessionOf<TState>> BeginSessionFor(
        string selector, bool throwIfNotExists = false, DateTime? appliesAt = null)
    {
        var connection = _connectionFactory();
        var session = new SqlEventStoreSession<TState>(_config, connection, selector);
        await session.Initialize();

        if (throwIfNotExists && session.IsNewState)
            throw new StreamNotFoundException(selector);

        return session;
    }

    public Task Delete(string selector) { /* ... */ }
    public Task<bool> Contains(string selector) { /* ... */ }
}
```

See the [EventStore integration tests](src/BullOak.Test.EventStore.Integration/) for a complete example of bridging BullOak with EventStoreDB, including reading events via `EventStoreClient.ReadStreamAsync()` and using `StateRehydrator.RehydrateFrom<TState>()` to reconstruct state.

---

## Project Structure

```
BullOak/
├── src/
│   ├── BullOak.Repositories/                    # Core library (net8.0)
│   │   ├── Configuration.cs                     # Entry point: Configuration.Begin()
│   │   ├── ConfigurationOwner.cs                # Internal builder that carries state through the chain
│   │   ├── Config/                              # Fluent builder step interfaces
│   │   │   ├── IConfigureEventCollectionType.cs
│   │   │   ├── IConfigureStateFactory.cs
│   │   │   ├── IConfigureThreadSafety.cs
│   │   │   ├── IConfigureEventPublisher.cs
│   │   │   ├── IConfigureEventAppliersAndBuild.cs
│   │   │   └── IHoldAllConfiguration.cs         # The final configuration bag
│   │   ├── Session/                             # Session lifecycle (unit of work)
│   │   │   ├── IManageSessionOf.cs              # Public session interface
│   │   │   ├── BaseRepoSession.cs               # Abstract: publish/save/intercept logic
│   │   │   ├── BaseEventSourcedSession.cs       # Abstract: adds LoadFromEvents() for rehydration
│   │   │   ├── CustomLinkedList/                 # SpinLock-backed event collection
│   │   │   ├── IValidateState.cs                # State validation contract
│   │   │   ├── ValidationResults.cs             # Success/error result type
│   │   │   └── BusinessException.cs             # Thrown when validation fails
│   │   ├── Appliers/                            # Event application
│   │   │   ├── IApplyEvents.cs                  # IApplyEvent<TState,TEvent>, IApplyEvents<TState>
│   │   │   ├── BaseApplyEvents.cs               # Abstract convenience base class
│   │   │   ├── FuncEventApplier.cs              # Lambda wrapper with implicit conversion
│   │   │   ├── StoredEvent.cs                   # Event + type + index struct
│   │   │   └── EventApplier.cs (internal)       # Runtime dispatcher with lazy caching
│   │   ├── Rehydration/                         # State reconstitution
│   │   │   ├── IRehydrateState.cs               # Rehydration contract
│   │   │   └── Rehydrator.cs                    # Upconvert → Create state → Apply events
│   │   ├── StateEmit/                           # Dynamic type generation (IL emission)
│   │   │   ├── EmittedTypeFactory.cs            # Generates concrete classes from interfaces
│   │   │   ├── ICanSwitchBackAndToReadOnly.cs   # Interface for the canEdit flag
│   │   │   └── Emitters/                        # IL code generators
│   │   │       ├── StateTypeEmitter.cs          # Creates dynamic assemblies and types
│   │   │       ├── OwnedStateClassEmitter.cs    # Generates standalone implementation classes
│   │   │       ├── StateWrapperEmitter.cs       # Generates wrappers around existing instances
│   │   │       └── InterfaceFlattener.cs        # Recursively collects properties from interface hierarchy
│   │   ├── Upconverting/                        # Event schema evolution
│   │   │   ├── IUpconvertEvent.cs               # One-to-one and one-to-many interfaces
│   │   │   ├── EventUpconverter.cs              # Runtime recursive upconversion
│   │   │   └── UpconverterCompiler.cs           # Compile-time validation and compilation
│   │   ├── EventPublisher/                      # External message bus integration
│   │   │   └── IPublishEvents.cs                # Async/sync publisher contract
│   │   ├── Middleware/                           # Event interceptors
│   │   │   └── IInterceptEvents.cs              # Four lifecycle hooks per event
│   │   ├── InMemory/                            # In-memory repository implementation
│   │   │   └── InMemoryEventSourcedRepository.cs
│   │   ├── Repository/                          # Repository interfaces
│   │   │   └── IStartSessions.cs
│   │   ├── Exceptions/
│   │   │   ├── ConcurrencyException.cs
│   │   │   └── StreamNotFoundException.cs
│   │   ├── ItemWithType.cs                      # Event + runtime Type struct
│   │   └── DeliveryTargetGuarntee.cs            # AtLeastOnce / AtMostOnce enum
│   │
│   ├── BullOak.Repositories.Test.Unit/          # 97 unit tests (xUnit + FluentAssertions + FakeItEasy)
│   │                                            #   Tests: session lifecycle, applier caching, state emission,
│   │                                            #   upconverter compilation/chaining, interceptor ordering,
│   │                                            #   custom linked list, concurrency, async loading
│   │
│   ├── BullOak.Repositories.Test.Acceptance/    # 16 BDD acceptance tests (SpecFlow + xUnit)
│   │                                            #   Tests: reconstitute state, save events, upconversion,
│   │                                            #   interceptors, validation, point-in-time queries,
│   │                                            #   IsNewState behavior
│   │
│   ├── BullOak.Repositories.Test.Unit.UpconverterContainer/
│   │                                            # Separate assembly used as a fixture for testing
│   │                                            # assembly-scanning of upconverters across access modifiers
│   │
│   ├── BullOak.Test.EndToEnd/                   # 3 end-to-end tests (SpecFlow + xUnit)
│   │                                            #   Domain: Cinema reservation system
│   │                                            #   Tests: create aggregate, reconstitute, child entity operations
│   │
│   ├── BullOak.Test.Benchmark/                  # Performance benchmarks (BenchmarkDotNet)
│   │                                            #   Benchmarks: aggregate loading, saving, child entity editing,
│   │                                            #   applier scaling, custom collection performance
│   │
│   └── BullOak.Test.EventStore.Integration/     # 15 EventStoreDB integration tests (Docker + TestContainers)
│                                                #   Tests: write/read events, subscriptions, projections,
│                                                #   BullOak state rehydration from EventStoreDB
│
├── .github/workflows/ci.yml                     # Unified CI: build → unit/acceptance/e2e/integration tests
├── CLAUDE.md                                    # Claude Code project instructions
└── README.md                                    # This file
```

---

## How It Works Internally

This section explains BullOak's internal mechanisms in detail. Understanding this is not required for using the library, but it helps when debugging, extending, or contributing.

### State Rehydration Flow

When you call `repo.BeginSessionFor(id)`, here is exactly what happens step by step:

```
1. Repository looks up the event stream for the given ID in ConcurrentDictionary
   ├─ If found: retrieve the List<(StoredEvent, DateTime)>
   └─ If not found: create a new empty list

2. Filter events by appliesAt timestamp (if provided)
   └─ Only include events where event.DateTime <= appliesAt

3. Create a new InMemoryEventStoreSession
   └─ Record initialVersion = stream.Count (for optimistic concurrency later)

4. Call session.LoadFromEvents(storedEvents)
   │
   ▼
5. Rehydrator.RehydrateFrom<TState>(storedEvents) is called
   │
   ├─ 5a. UPCONVERT: Each StoredEvent passes through the registered upconverters
   │       - EventUpconverter.Upconvert() checks each event's Type against the
   │         upconverter dictionary (keyed by source Type)
   │       - If a match exists: call the upconverter function
   │       - If the result is a one-to-many upconversion: each result gets its own
   │         recursive upconversion pass (chains of arbitrary depth)
   │       - If no match: event passes through unchanged
   │       - The EventIndex from the original event is preserved on all results
   │
   ├─ 5b. CREATE INITIAL STATE: StateFactory.GetState(typeof(TState))
   │       - For interfaces: EmittedTypeFactory generates a class via IL emission
   │         (cached in ConcurrentDictionary — generation happens only once per type)
   │       - For classes: creates a DynamicMethod that calls the parameterless constructor
   │         (faster than Activator.CreateInstance)
   │       - For structs: uses Activator.CreateInstance once, then returns copies
   │
   └─ 5c. APPLY EVENTS: EventApplier.Apply(stateType, initialState, upconvertedEvents)
          │
          ├─ If state implements ICanSwitchBackAndToReadOnly:
          │    Set CanEdit = true (unlock the state for mutation)
          │
          ├─ For each StoredEvent in order:
          │   ├─ Look up (stateType, eventType) in the indexed cache Dictionary
          │   ├─ If cache miss: scan all registered ApplierRetrievers (linear search)
          │   │   ├─ Found: cache the result under (stateType, eventType)
          │   │   └─ Not found: throw ApplierNotFoundException
          │   └─ Call applier.Apply(state, event) → returns the (possibly mutated) state
          │
          ├─ If state implements ICanSwitchBackAndToReadOnly:
          │    Set CanEdit = false (lock the state — setters will now throw)
          │
          └─ Return RehydrateFromResult<TState> containing:
             - State: the fully reconstituted state object
             - IsStateDefault: true if no events were applied (empty stream)
             - LastEventIndex: index of the last applied event (for concurrency)

6. Session stores the reconstituted state and concurrencyId
7. session.GetCurrentState() returns the fully reconstructed state
```

### SaveChanges Flow

When you call `session.SaveChanges()`:

```
1. RUN VALIDATOR (if configured)
   ├─ Call IValidateState<TState>.Validate(currentState)
   ├─ If ValidationResults.IsSuccess: continue
   └─ If ValidationResults has errors: throw AggregateException containing
      BusinessException for each IValidationError. NO events are published or saved.

2. SNAPSHOT the new events: ItemWithType[] newEvents = newEventsCollection.ToArray()

3. FOR DeliveryTargetGuarntee.AtLeastOnce (default):
   │
   ├─ For each event in newEvents:
   │   ├─ Call each interceptor's BeforePublish(event, eventType, state, stateType)
   │   ├─ Call eventPublisher.Publish(event, cancellationToken) or PublishSync(event)
   │   └─ Call each interceptor's AfterPublish(event, eventType, state, stateType)
   │
   ├─ For each event in newEvents:
   │   └─ Call each interceptor's BeforeSave(event, eventType, state, stateType)
   │
   ├─ Call the abstract SaveChanges(newEvents, currentState, cancellationToken)
   │   └─ InMemoryEventStoreSession: appends to List under lock, checks concurrency
   │
   └─ For each event in newEvents:
       └─ Call each interceptor's AfterSave(event, eventType, state, stateType)

   FOR DeliveryTargetGuarntee.AtMostOnce: same steps but Save happens before Publish

4. Clear the new-events collection (LinkedList.Clear())
5. Update initialVersion for future concurrency checks
```

### Dynamic Type Emission

When `EmittedTypeFactory` encounters an interface type for the first time:

```
1. Check ConcurrentDictionary<Type, Func<object>> cache → return if already generated

2. Call StateTypeEmitter.EmitType(interfaceType, new OwnedStateClassEmitter())
   │
   ├─ 2a. Create a new dynamic in-memory assembly
   │       AssemblyBuilder with AssemblyBuilderAccess.Run (not saved to disk)
   │
   ├─ 2b. InterfaceFlattener.Flatten(interfaceType):
   │       - Recursively walk the interface and all its base interfaces
   │       - Collect all properties from the entire hierarchy
   │       - De-duplicate: properties with same (name, type) → one backing field
   │       - Properties with same name but different types → separate backing fields
   │
   ├─ 2c. Define the new type:
   │       - Class name: "OwneddStateEmitter_{incrementingIndex}"
   │       - Implements: the original interface + all inherited interfaces
   │       - Also implements: ICanSwitchBackAndToReadOnly
   │
   ├─ 2d. For each unique property:
   │       ├─ Define a private backing field (e.g., _accountId)
   │       ├─ Define a getter method: return _accountId;
   │       └─ Define a setter method with guard IL:
   │           if (!this._canEdit)
   │               throw new Exception("You can only edit this item during reconstitution");
   │           this._accountId = value;
   │
   ├─ 2e. Define ICanSwitchBackAndToReadOnly.CanEdit setter:
   │       this._canEdit = value;
   │
   └─ 2f. Create the Type via TypeBuilder.CreateType()

3. Create a DynamicMethod that calls the new type's parameterless constructor
   (this is faster than Activator.CreateInstance — avoids reflection on every call)

4. Cache Func<object> in the ConcurrentDictionary for future use
```

### Applier Resolution and Caching

The internal `EventApplier` class uses a two-tier lookup strategy to find the right applier for a given `(stateType, eventType)` pair:

```
Tier 1: Indexed cache (Dictionary<EventAndStateTypes, ApplierRetriever>)
  - O(1) lookup after first use
  - Thread-safe via lock + double-checked locking pattern

Tier 2: Linear scan of all registered ApplierRetrievers
  - Used on cache miss
  - Each ApplierRetriever can check if it handles the given (stateType, eventType) pair
  - Result is cached in Tier 1 for all future calls

Example flow:

1. First call with (AccountState, MoneyDeposited):
   - Tier 1: cache miss
   - Tier 2: scan all registered appliers
   - Find MoneyDepositedApplier (implements IApplyEvent<AccountState, MoneyDeposited>)
   - Cache under (AccountState, MoneyDeposited)
   - Call Apply() and return result

2. Second call with same types:
   - Tier 1: cache HIT → return immediately (O(1) dictionary lookup)

3. Call with unregistered (AccountState, UnknownEvent):
   - Tier 1: cache miss
   - Tier 2: scan all appliers → none found
   - Throw ApplierNotFoundException with descriptive message

Performance: after warm-up (one call per unique type pair), applier dispatch is a dictionary
lookup — essentially free. The warm-up cost is one linear scan through registered appliers.
```

---

## Key Types Reference

All public types in the `BullOak.Repositories` namespace:

### Configuration

| Type | Kind | Description |
|------|------|-------------|
| `Configuration` | static class | Entry point. Call `Configuration.Begin()` to start the fluent builder chain. |
| `IHoldAllConfiguration` | interface | The final configuration bag holding all components. Passed to repository constructors. |
| `IConfigureBullOak` | interface | Base interface for all builder steps. Exposes `AddInterceptor()`. |

### Session

| Type | Kind | Description |
|------|------|-------------|
| `IManageSessionOf<TState>` | interface | Public session contract: `GetCurrentState()`, `AddEvent()`, `SaveChanges()`, `IsNewState`. |
| `BaseRepoSession<TState>` | abstract class | Implements publish/save/intercept logic. Override `SaveChanges(ItemWithType[], TState, CancellationToken)` for custom stores. |
| `BaseEventSourcedSession<TState>` | abstract class | Extends `BaseRepoSession`. Adds `LoadFromEvents()` methods for rehydration from `StoredEvent[]`, `IEnumerable<StoredEvent>`, or `IAsyncEnumerable<StoredEvent>`. |

### Appliers

| Type | Kind | Description |
|------|------|-------------|
| `IApplyEvent<TState, TEvent>` | interface | Handles one specific event type. Method: `TState Apply(TState, TEvent)`. |
| `IApplyEvents<TState>` | interface | Handles multiple event types via switch. Methods: `bool CanApplyEvent(Type)`, `TState Apply(TState, object)`. |
| `BaseApplyEvents<TState, TEvent>` | abstract class | Convenience base implementing `IApplyEvent<TState, TEvent>`. Override `Apply()`. |
| `FuncEventApplier<TState, TEvent>` | class | Wraps a `Func<TState, TEvent, TState>` lambda. Has implicit conversion from the func type. |
| `StoredEvent` | struct | Persisted event: `EventType` (Type), `Event` (object), `EventIndex` (long). |
| `ItemWithType` | struct | Event + runtime Type pair: `instance` (object), `type` (Type). |

### Rehydration

| Type | Kind | Description |
|------|------|-------------|
| `IRehydrateState` | interface | Contract for state reconstitution. |
| `Rehydrator` | class | Concrete implementation: upconvert → create state → apply events. |
| `RehydrateFromResult<TState>` | struct | Result of rehydration: `State`, `IsStateDefault`, `LastEventIndex`. |

### State Emission

| Type | Kind | Description |
|------|------|-------------|
| `ICreateStateInstances` | interface | Factory contract for creating state instances. |
| `ICanSwitchBackAndToReadOnly` | interface | Write-only `CanEdit` property. Generated classes implement this to control setter guards. |

### Upconversion

| Type | Kind | Description |
|------|------|-------------|
| `IUpconvertEvent<TSource, TDest>` | interface | One-to-one upconversion: `TDest Upconvert(TSource)`. |
| `IUpconvertEvent<TSource>` | interface | One-to-many upconversion: `IEnumerable<object> Upconvert(TSource)`. |

### Publishing & Interception

| Type | Kind | Description |
|------|------|-------------|
| `IPublishEvents` | interface | Event publisher contract: `Task Publish(ItemWithType, CancellationToken)`, `void PublishSync(ItemWithType)`. |
| `IInterceptEvents` | interface | Four hooks: `BeforePublish`, `AfterPublish`, `BeforeSave`, `AfterSave`. Each receives the event, its type, the current state, and the state type. |

### Validation

| Type | Kind | Description |
|------|------|-------------|
| `IValidateState<TState>` | interface | Validates state before save: `ValidationResults Validate(TState)`. |
| `ValidationResults` | struct | `IsSuccess` bool + `IEnumerable<IValidationError>` errors. Static factories: `Success()`, `Errors(BasicValidationError[])`. |
| `BasicValidationError` | class | Simple validation error with a message. Has implicit conversion from `string`. |

### Repository

| Type | Kind | Description |
|------|------|-------------|
| `IStartSessions<TId, TState>` | interface | Repository contract: `BeginSessionFor()`, `Delete()`, `Contains()`. |
| `InMemoryEventSourcedRepository<TId, TState>` | class | Complete in-memory implementation backed by `ConcurrentDictionary`. |

### Enums & Exceptions

| Type | Kind | Description |
|------|------|-------------|
| `DeliveryTargetGuarntee` | enum | `AtLeastOnce` (publish before save), `AtMostOnce` (save before publish). |
| `ConcurrencyException` | exception | Thrown when a concurrent write is detected during `SaveChanges()`. |
| `StreamNotFoundException` | exception | Thrown when `BeginSessionFor(id, throwIfNotExists: true)` finds no events. |
| `BusinessException` | exception | Wraps an `IValidationError`. Thrown (inside `AggregateException`) when validation fails. |
| `ApplierNotFoundException` | exception | Thrown when no applier is registered for a `(stateType, eventType)` pair. |
| `PreflightUpconverterConflictException` | exception | Thrown at configuration time when two upconverters have the same source event type. |

---

## Error Handling

BullOak throws specific exceptions for different failure scenarios. Here is when each exception is thrown and how to handle it:

| Exception | When Thrown | How to Handle |
|-----------|-----------|---------------|
| `ApplierNotFoundException` | During rehydration or `AddEvent()`, when no applier is registered for the `(stateType, eventType)` pair. | Register an applier for every event type your aggregates use. This is a developer error — fix it in code. |
| `ConcurrencyException` | During `SaveChanges()`, when another session wrote to the same stream between your load and save. | Reload the aggregate, re-apply your domain logic against the fresh state, and try again (optimistic retry pattern). |
| `StreamNotFoundException` | During `BeginSessionFor(id, throwIfNotExists: true)`, when the stream has no events. | Only thrown when you explicitly set `throwIfNotExists: true`. Use this to guard against operations on non-existent aggregates. |
| `AggregateException` (containing `BusinessException`) | During `SaveChanges()`, when the state validator returns errors. | Catch `AggregateException`, inspect `InnerExceptions` for `BusinessException` instances. Each wraps an `IValidationError`. Report to the user or log. |
| `PreflightUpconverterConflictException` | During configuration (`.AndNoMoreUpconverters()`), when two upconverters share the same source event type. | Fix the upconverter registration — each source event type can have at most one upconverter. |
| `Exception("You can only edit this item during reconstitution")` | When application code tries to set a property on an interface-based state object outside of an applier. | This means you're mutating state directly instead of through events. Record an event with `AddEvent()` instead. |

---

## Running the Tests

```bash
# All tests except Docker-dependent ones (97 unit + 16 acceptance + 3 e2e = 116 tests)
dotnet test src/BullOak.sln --filter "FullyQualifiedName!~EventStore.Integration"

# Just unit tests (97 tests — fast, no dependencies)
dotnet test src/BullOak.Repositories.Test.Unit

# Just BDD acceptance tests (16 tests — SpecFlow scenarios)
dotnet test src/BullOak.Repositories.Test.Acceptance

# Just end-to-end tests (3 tests — cinema reservation domain)
dotnet test src/BullOak.Test.EndToEnd

# EventStore integration tests (15 tests — requires Docker Desktop running)
# Spins up a real EventStoreDB instance via TestContainers
dotnet test src/BullOak.Test.EventStore.Integration

# Run performance benchmarks
dotnet run -c Release --project src/BullOak.Test.Benchmark
```

---

## FAQ

**Q: Do I have to use the in-memory repository?**
No. The in-memory repository is provided for testing and prototyping. For production, extend `BaseEventSourcedSession<TState>` to integrate with any event store — you only need to implement `LoadFromEvents()` and `SaveChanges()`. See [Integrating with External Event Stores](#integrating-with-external-event-stores).

**Q: Can I use classes instead of interfaces for state?**
Yes. Classes work fine and are simpler to start with. The advantage of interfaces is that BullOak generates a class with built-in write protection — properties can only be set during event application. This prevents accidental state mutation. With classes, there is no such protection; your discipline must ensure state is only mutated through events.

**Q: What happens if I forget to register an applier for an event type?**
BullOak throws `ApplierNotFoundException` when it tries to apply that event during rehydration or `AddEvent()`. This is a runtime error, not a compile-time one. To catch this early, write integration tests that exercise all your event types.

**Q: Can I call `SaveChanges()` multiple times on the same session?**
Yes. After each save, the new-events collection is cleared and the concurrency version is updated. You can continue adding events and saving within the same session.

**Q: What happens if I dispose a session without saving?**
Nothing is persisted. This is by design — dispose-without-save is the explicit rollback/discard pattern. BullOak does not auto-save.

**Q: How do I handle event versioning / schema changes?**
Use upconverters. They transform old events into current ones at load time, without modifying the stored events. Your appliers only ever see the current event schemas. See [Event Upconversion](#event-upconversion-schema-evolution).

**Q: Is BullOak thread-safe?**
Sessions are designed for single-threaded use (one session per aggregate per request). The in-memory repository itself is thread-safe (backed by `ConcurrentDictionary` with `lock` on individual streams). If you need multiple threads to add events to the same session concurrently (unusual), configure `AlwaysUseThreadSafe()`.

**Q: How does BullOak handle large event streams?**
BullOak supports `IAsyncEnumerable<StoredEvent>` for lazy, streaming event loading. This avoids loading all events into memory at once. Use the `BaseEventSourcedSession.LoadFromEvents(IAsyncEnumerable<StoredEvent>)` overload. For very large streams, consider implementing snapshots in your custom session — load the snapshot, then replay only events after the snapshot point.

**Q: Can I use BullOak with dependency injection (e.g., Microsoft.Extensions.DependencyInjection)?**
Yes. Use `.WithAnyAppliersFromInstances(applierInstances)` to pass pre-created applier objects (resolved from your DI container) instead of assembly scanning. For the state factory, use `.With(type => () => serviceProvider.GetRequiredService(type))` to delegate state creation to your container.

**Q: What is the performance overhead of replaying events?**
Minimal. After the first call for a given `(stateType, eventType)` pair, applier dispatch is a dictionary lookup. The benchmarks in `BullOak.Test.Benchmark` show that loading an aggregate with 10 events takes microseconds. For streams with thousands of events, consider using snapshots.

---

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-change`)
3. Make your changes
4. Run all tests: `dotnet test src/BullOak.sln`
5. Open a pull request against `master`

For bugs and feature requests, [open an issue](https://github.com/vaibhavPH/BullOak/issues).
