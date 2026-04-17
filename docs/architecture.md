# BullOak Architecture Diagrams

This document provides a visual overview of BullOak — a .NET event-sourcing library — using Mermaid diagrams. It is intended as a fast onboarding reference that complements the in-depth prose in the root [`README.md`](../README.md).

All diagrams render natively on GitHub. If you are viewing this outside GitHub, any Mermaid-aware renderer (VS Code preview, Obsidian, mermaid.live) will work.

---

## 1. System Context

Where BullOak sits between a user's application and the backing event store / messaging infrastructure.

```mermaid
graph TD
    User["User Application<br/>(domain code: aggregates, events, appliers)"]
    Core["BullOak.Repositories<br/>(core library — .NET 8)"]
    PG["BullOak.Repositories.PostgreSql<br/>(adapter — .NET 8)"]
    InMem["InMemory repository<br/>(built into core)"]
    ESBridge["EventStoreDB bridge<br/>(demonstrated via integration tests)"]
    PGDB[("PostgreSQL")]
    ESDB[("EventStoreDB")]
    RMQ[("RabbitMQ<br/>(optional publish sink)")]

    User -->|fluent configure + session API| Core
    Core --> InMem
    Core --> PG
    PG --> PGDB
    Core -.via bridge.-> ESBridge
    ESBridge --> ESDB
    Core -.IPublishEvents.-> RMQ
```

**Reading it:** solid arrows are first-class runtime dependencies; dashed arrows are optional / pluggable integrations exercised by the integration test suite.

---

## 2. Core Component Map

The internal components of `BullOak.Repositories` and how they collaborate at runtime.

```mermaid
graph LR
    subgraph Configuration
        Config["IHoldAllConfiguration<br/>(fluent builder)"]
    end

    subgraph "API surface"
        Repo["Repository<br/>IStartSessions&lt;TSel,TState&gt;"]
        Sess["Session<br/>IManageSessionOf&lt;TState&gt;"]
    end

    subgraph "Rehydration pipeline"
        Rehy["IRehydrateState"]
        App["IApplyEvents&lt;TState&gt;"]
        Up["IUpconvertEvent&lt;T&gt;"]
    end

    subgraph "State construction"
        Factory["ICreateStateInstances<br/>(IL-emitted types)"]
        Writ["IControlStateWritability"]
    end

    subgraph "Cross-cutting"
        Pub["IPublishEvents"]
        Int["IInterceptEvents"]
        Val["IValidateState&lt;TState&gt;"]
    end

    Config --> Repo
    Repo -->|BeginSessionFor| Sess
    Sess --> Rehy
    Rehy --> Up
    Rehy --> App
    App --> Factory
    Factory --> Writ
    Sess -->|on SaveChanges| Pub
    Sess -->|before/after hooks| Int
    Sess -->|invariants| Val
```

**Reading it:** the session is the unit of work. Rehydration is a read-side pipeline (events → state); save-time concerns (publish, intercept, validate) are orthogonal cross-cutting services.

---

## 3. Key Abstractions — Class Diagram

The public interfaces that define BullOak's extensibility points.

```mermaid
classDiagram
    class IStartSessions~TSel_TState~ {
        +BeginSessionFor(id, throwIfNotExists, appliesAt)
        +Delete(id)
        +Contains(id)
    }
    class IManageSessionOf~TState~ {
        +bool IsNewState
        +GetCurrentState() TState
        +AddEvent(evt)
        +AddEvents(evts)
        +SaveChanges(guarantee, ct) int
    }
    class IApplyEvents~TState~ {
        +Apply(state, evt) TState
    }
    class IRehydrateState {
        +RehydrateFrom~TState~(events) RehydrateFromResult
    }
    class ICreateStateInstances {
        +WarmupWith(type)
        +GetState(type)
        +GetWrapper~T~()
    }
    class IPublishEvents {
        +Publish(evt) Task
        +PublishSync(evt)
    }
    class IInterceptEvents {
        +BeforeSave(evt)
        +AfterSave(evt)
        +BeforePublish(evt)
        +AfterPublish(evt)
    }
    class IUpconvertEvent~TSource~ {
        +Upconvert(src) IEnumerable
    }
    class IValidateState~TState~ {
        +Validate(state)
    }

    IStartSessions --> IManageSessionOf : creates
    IManageSessionOf --> IRehydrateState : uses on load
    IRehydrateState --> IApplyEvents : invokes per event
    IRehydrateState --> ICreateStateInstances : builds empty state
    IRehydrateState --> IUpconvertEvent : transforms legacy events
    IManageSessionOf --> IPublishEvents : on save
    IManageSessionOf --> IInterceptEvents : around save and publish
    IManageSessionOf --> IValidateState : invariant check
```

**Reading it:** every dependency here is injectable via the fluent `IHoldAllConfiguration` builder — production code swaps these to change persistence, publishing, or event-schema evolution strategy.

---

## 4. Sequence: Load / Rehydrate a Session

What happens when a user calls `repository.BeginSessionFor(id)`.

```mermaid
sequenceDiagram
    actor U as User code
    participant R as Repository<br/>(IStartSessions)
    participant ES as Event Store<br/>(PG / InMem / ESDB)
    participant S as Session<br/>(IManageSessionOf)
    participant H as IRehydrateState
    participant Up as Upconverters
    participant A as Appliers<br/>(IApplyEvents)
    participant F as StateFactory

    U->>R: BeginSessionFor(id, throwIfNotExists, appliesAt?)
    R->>ES: Read stream for id
    ES-->>R: StoredEvent[] + lastEventIndex
    R->>S: new Session(events, lastIndex)
    S->>H: RehydrateFrom<TState>(events)
    H->>F: GetState(typeof(TState))
    F-->>H: empty state instance
    loop each event up to appliesAt
        H->>Up: Upconvert(raw) → current-schema events
        Up-->>H: one or more events
        H->>A: Apply(state, event)
        A-->>H: updated state
    end
    H-->>S: RehydrateFromResult(state, isNew, lastIndex)
    S-->>U: ready-to-use session
```

**Reading it:** point-in-time queries are served by truncating the replay loop at `appliesAt` — the store still returns the full stream, but the rehydrator stops early.

---

## 5. Sequence: Apply New Events and Save

`session.AddEvent(...)` followed by `session.SaveChanges(...)`.

```mermaid
sequenceDiagram
    actor U as User code
    participant S as Session
    participant A as Appliers
    participant V as IValidateState
    participant I as IInterceptEvents
    participant ES as Event Store
    participant P as IPublishEvents

    U->>S: AddEvent(e)
    S->>A: Apply(state, e)
    A-->>S: updated in-memory state
    Note over S: e queued in NewEventsCollection

    U->>S: SaveChanges(guarantee, ct)
    S->>S: check concurrency<br/>(lastPersistedVersion == concurrencyId)
    alt version mismatch
        S-->>U: throw ConcurrencyException
    else ok
        S->>V: Validate(state)
        loop each new event
            S->>I: BeforeSave(evt)
        end
        S->>ES: Append new events atomically
        ES-->>S: ok
        loop each new event
            S->>I: AfterSave(evt)
            S->>P: Publish(evt)
            P->>I: BeforePublish(evt) / AfterPublish(evt)
        end
        S-->>U: persisted count
    end
```

**Reading it:** state mutation happens *immediately* on `AddEvent` (optimistic), but persistence and publishing only happen on `SaveChanges`. The concurrency check is the mechanism that prevents lost updates across concurrent sessions for the same aggregate.

---

## 6. Solution / Project Structure

The .csproj graph — library, adapter, sample, and test projects, with dependency edges.

```mermaid
graph TD
    Core["BullOak.Repositories<br/>(core — .NET 8)"]
    PG["BullOak.Repositories.PostgreSql<br/>(adapter — .NET 8)"]
    Console["BullOak.Console<br/>(sample — .NET 8)"]

    UT["BullOak.Repositories.Test.Unit"]
    UTUp["BullOak.Repositories.Test.Unit.UpconverterContainer"]
    Acc["BullOak.Repositories.Test.Acceptance"]
    E2E["BullOak.Test.EndToEnd"]
    Bench["BullOak.Test.Benchmark"]
    ESInt["BullOak.Test.EventStore.Integration<br/>(net6.0 — TestContainers)"]
    PGInt["BullOak.Test.PostgreSql.Integration<br/>(TestContainers)"]
    RMQInt["BullOak.Test.RabbitMq.Integration<br/>(TestContainers)"]
    RMInt["BullOak.Test.ReadModel.Integration<br/>(TestContainers)"]

    PG --> Core
    Console --> Core
    UT --> Core
    UTUp --> Core
    Acc --> Core
    E2E --> Core
    Bench --> Core
    ESInt --> Core
    PGInt --> Core
    PGInt --> PG
    RMQInt --> Core
    RMInt --> Core
    RMInt --> PG

    classDef lib fill:#dbeafe,stroke:#1e3a8a
    classDef test fill:#fef3c7,stroke:#92400e
    classDef sample fill:#dcfce7,stroke:#166534
    class Core,PG lib
    class Console sample
    class UT,UTUp,Acc,E2E,Bench,ESInt,PGInt,RMQInt,RMInt test
```

**Reading it:** the fan-in to `Core` shows how small the library's public surface must stay — every test project and adapter couples to it.

---

## 7. CI Pipeline

The GitHub Actions workflow at `.github/workflows/ci.yml`.

```mermaid
flowchart LR
    Start([push / PR]) --> Build["Build<br/>dotnet restore + build"]
    Build --> Unit["Unit Tests"]
    Build --> Acc["Acceptance Tests"]
    Build --> E2E["End-to-End Tests"]
    Build --> Docker{{"Docker-based<br/>Integration Tests"}}
    Docker --> ESDB["EventStoreDB<br/>(TestContainers)"]
    Docker --> PG["PostgreSQL<br/>(postgres:16-alpine)"]
    Docker --> RMQ["RabbitMQ<br/>(rabbitmq:3-management)"]
    Docker --> RM["Read Model<br/>(postgres:16-alpine)"]

    Unit --> Artifacts[["Upload .trx artifacts<br/>(30-day retention)"]]
    Acc --> Artifacts
    E2E --> Artifacts
    ESDB --> Artifacts
    PG --> Artifacts
    RMQ --> Artifacts
    RM --> Artifacts
```

**Reading it:** Docker tests are parallelizable because each TestContainers fixture is isolated per test project. Artifact upload is a fan-in sink used for post-run inspection.

---

## Updating these diagrams

When any of the following change, this document should be refreshed:

- Public interfaces in `src/BullOak.Repositories/` — affects diagrams 2 and 3.
- New persistence adapter added — affects diagrams 1 and 6.
- Session load/save behaviour changes (e.g., batching, new hook points) — affects diagrams 4 and 5.
- CI workflow in `.github/workflows/ci.yml` gains or drops jobs — affects diagram 7.

Prefer updating this file in the same PR as the code change so the diagrams do not drift.
