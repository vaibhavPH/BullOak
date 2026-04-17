# BullOak Architecture Diagrams

This document provides a visual overview of BullOak — a .NET event-sourcing library — using Mermaid diagrams. It is intended as a fast onboarding reference that complements the in-depth prose in the root [`README.md`](../README.md).

All diagrams render natively on GitHub. If you are viewing this outside GitHub, any Mermaid-aware renderer (VS Code preview, Obsidian, mermaid.live) will work.

## Colour legend

A consistent palette is used across every diagram so the same colour always means the same kind of thing:

| Colour | Meaning |
|---|---|
| 🟦 Blue | BullOak core library |
| 🟪 Purple | Adapters (persistence / bridges) |
| 🟧 Orange | Runtime session / rehydration pipeline |
| 🟨 Amber | Backing data stores |
| 🟩 Green | External user code / actors |
| 🩷 Pink | Messaging / cross-cutting services |
| 🟫 Yellow | Test projects |
| 🩵 Cyan | CI pipeline steps |

---

## 1. System Context

Where BullOak sits between a user's application and the backing event store / messaging infrastructure.

```mermaid
graph TD
    User["User Application<br/>(domain code: aggregates, events, appliers)"]:::actor
    Core["BullOak.Repositories<br/>(core library — .NET 8)"]:::core
    PG["BullOak.Repositories.PostgreSql<br/>(adapter — .NET 8)"]:::adapter
    InMem["InMemory repository<br/>(built into core)"]:::adapter
    ESBridge["EventStoreDB bridge<br/>(demonstrated via integration tests)"]:::adapter
    PGDB[("PostgreSQL")]:::store
    ESDB[("EventStoreDB")]:::store
    RMQ[("RabbitMQ<br/>(optional publish sink)")]:::msg

    User -->|fluent configure + session API| Core
    Core --> InMem
    Core --> PG
    PG --> PGDB
    Core -.via bridge.-> ESBridge
    ESBridge --> ESDB
    Core -.IPublishEvents.-> RMQ

    classDef actor fill:#dcfce7,stroke:#166534,stroke-width:2px,color:#064e3b
    classDef core fill:#dbeafe,stroke:#1e3a8a,stroke-width:2.5px,color:#1e293b
    classDef adapter fill:#f3e8ff,stroke:#6b21a8,stroke-width:1.5px,color:#581c87
    classDef store fill:#fef3c7,stroke:#92400e,stroke-width:1.5px,color:#78350f
    classDef msg fill:#fce7f3,stroke:#9d174d,stroke-width:1.5px,color:#831843
```

**Reading it:** solid arrows are first-class runtime dependencies; dashed arrows are optional / pluggable integrations exercised by the integration test suite.

---

## 2. Core Component Map

The internal components of `BullOak.Repositories` and how they collaborate at runtime.

```mermaid
graph LR
    subgraph Configuration
        Config["IHoldAllConfiguration<br/>(fluent builder)"]:::cfg
    end

    subgraph "API surface"
        Repo["Repository<br/>IStartSessions&lt;TSel,TState&gt;"]:::core
        Sess["Session<br/>IManageSessionOf&lt;TState&gt;"]:::runtime
    end

    subgraph "Rehydration pipeline"
        Rehy["IRehydrateState"]:::rehy
        App["IApplyEvents&lt;TState&gt;"]:::rehy
        Up["IUpconvertEvent&lt;T&gt;"]:::rehy
    end

    subgraph "State construction"
        Factory["ICreateStateInstances<br/>(IL-emitted types)"]:::state
        Writ["IControlStateWritability"]:::state
    end

    subgraph "Cross-cutting"
        Pub["IPublishEvents"]:::xcut
        Int["IInterceptEvents"]:::xcut
        Val["IValidateState&lt;TState&gt;"]:::xcut
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

    classDef cfg fill:#e0e7ff,stroke:#3730a3,stroke-width:1.5px,color:#1e1b4b
    classDef core fill:#dbeafe,stroke:#1e3a8a,stroke-width:2px,color:#1e293b
    classDef runtime fill:#fed7aa,stroke:#9a3412,stroke-width:2px,color:#7c2d12
    classDef rehy fill:#f3e8ff,stroke:#6b21a8,stroke-width:1.5px,color:#581c87
    classDef state fill:#fef3c7,stroke:#92400e,stroke-width:1.5px,color:#78350f
    classDef xcut fill:#ccfbf1,stroke:#115e59,stroke-width:1.5px,color:#134e4a
```

**Reading it:** the session is the unit of work. Rehydration is a read-side pipeline (events → state); save-time concerns (publish, intercept, validate) are orthogonal cross-cutting services.

---

## 3. Key Abstractions — Class Diagram

The public interfaces that define BullOak's extensibility points.

```mermaid
%%{init: {"theme":"base","themeVariables":{"primaryColor":"#dbeafe","primaryTextColor":"#1e293b","primaryBorderColor":"#1e3a8a","lineColor":"#475569","secondaryColor":"#f3e8ff","tertiaryColor":"#ccfbf1","fontFamily":"ui-sans-serif, system-ui, sans-serif"}}}%%
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
%%{init: {"theme":"base","themeVariables":{"actorBkg":"#dbeafe","actorBorder":"#1e3a8a","actorTextColor":"#1e293b","actorLineColor":"#64748b","signalColor":"#475569","signalTextColor":"#1e293b","labelBoxBkgColor":"#fef3c7","labelBoxBorderColor":"#92400e","labelTextColor":"#78350f","loopTextColor":"#7c2d12","noteBkgColor":"#fef9c3","noteBorderColor":"#854d0e","noteTextColor":"#713f12","activationBkgColor":"#f3e8ff","activationBorderColor":"#6b21a8","sequenceNumberColor":"#ffffff"}}}%%
sequenceDiagram
    actor U as User code
    participant R as Repository<br/>(IStartSessions)
    participant ES as Event Store<br/>(PG / InMem / ESDB)
    participant S as Session<br/>(IManageSessionOf)
    participant H as IRehydrateState
    participant Up as Upconverters
    participant A as Appliers<br/>(IApplyEvents)
    participant F as StateFactory

    U->>+R: BeginSessionFor(id, throwIfNotExists, appliesAt?)
    R->>+ES: Read stream for id
    ES-->>-R: StoredEvent[] + lastEventIndex
    R->>+S: new Session(events, lastIndex)
    S->>+H: RehydrateFrom<TState>(events)
    H->>+F: GetState(typeof(TState))
    F-->>-H: empty state instance
    loop each event up to appliesAt
        H->>+Up: Upconvert(raw) → current-schema events
        Up-->>-H: one or more events
        H->>+A: Apply(state, event)
        A-->>-H: updated state
    end
    H-->>-S: RehydrateFromResult(state, isNew, lastIndex)
    S-->>-U: ready-to-use session
    deactivate R
```

**Reading it:** point-in-time queries are served by truncating the replay loop at `appliesAt` — the store still returns the full stream, but the rehydrator stops early.

---

## 5. Sequence: Apply New Events and Save

`session.AddEvent(...)` followed by `session.SaveChanges(...)`.

```mermaid
%%{init: {"theme":"base","themeVariables":{"actorBkg":"#dcfce7","actorBorder":"#166534","actorTextColor":"#064e3b","actorLineColor":"#64748b","signalColor":"#475569","signalTextColor":"#1e293b","labelBoxBkgColor":"#fce7f3","labelBoxBorderColor":"#9d174d","labelTextColor":"#831843","loopTextColor":"#7c2d12","noteBkgColor":"#fef9c3","noteBorderColor":"#854d0e","noteTextColor":"#713f12","altBackground":"#fef3c7","activationBkgColor":"#fed7aa","activationBorderColor":"#9a3412"}}}%%
sequenceDiagram
    actor U as User code
    participant S as Session
    participant A as Appliers
    participant V as IValidateState
    participant I as IInterceptEvents
    participant ES as Event Store
    participant P as IPublishEvents

    U->>+S: AddEvent(e)
    S->>+A: Apply(state, e)
    A-->>-S: updated in-memory state
    Note over S: e queued in NewEventsCollection
    S-->>-U: ok

    U->>+S: SaveChanges(guarantee, ct)
    S->>S: check concurrency<br/>(lastPersistedVersion == concurrencyId)
    alt version mismatch
        S-->>U: throw ConcurrencyException
    else ok
        S->>V: Validate(state)
        loop each new event
            S->>I: BeforeSave(evt)
        end
        S->>+ES: Append new events atomically
        ES-->>-S: ok
        loop each new event
            S->>I: AfterSave(evt)
            S->>+P: Publish(evt)
            P->>I: BeforePublish(evt) / AfterPublish(evt)
            P-->>-S: ok
        end
        S-->>-U: persisted count
    end
```

**Reading it:** state mutation happens *immediately* on `AddEvent` (optimistic), but persistence and publishing only happen on `SaveChanges`. The concurrency check is the mechanism that prevents lost updates across concurrent sessions for the same aggregate.

---

## 6. Solution / Project Structure

The .csproj graph — library, adapter, sample, and test projects, with dependency edges.

```mermaid
graph TD
    Core["BullOak.Repositories<br/>(core — .NET 8)"]:::lib
    PG["BullOak.Repositories.PostgreSql<br/>(adapter — .NET 8)"]:::adapter
    Console["BullOak.Console<br/>(sample — .NET 8)"]:::sample

    UT["BullOak.Repositories.Test.Unit"]:::unit
    UTUp["BullOak.Repositories.Test.Unit.UpconverterContainer"]:::unit
    Acc["BullOak.Repositories.Test.Acceptance"]:::unit
    E2E["BullOak.Test.EndToEnd"]:::unit
    Bench["BullOak.Test.Benchmark"]:::unit
    ESInt["BullOak.Test.EventStore.Integration<br/>(net6.0 — TestContainers)"]:::integ
    PGInt["BullOak.Test.PostgreSql.Integration<br/>(TestContainers)"]:::integ
    RMQInt["BullOak.Test.RabbitMq.Integration<br/>(TestContainers)"]:::integ
    RMInt["BullOak.Test.ReadModel.Integration<br/>(TestContainers)"]:::integ

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

    classDef lib fill:#dbeafe,stroke:#1e3a8a,stroke-width:2.5px,color:#1e293b
    classDef adapter fill:#f3e8ff,stroke:#6b21a8,stroke-width:2px,color:#581c87
    classDef sample fill:#dcfce7,stroke:#166534,stroke-width:1.5px,color:#064e3b
    classDef unit fill:#fef9c3,stroke:#854d0e,stroke-width:1.5px,color:#713f12
    classDef integ fill:#fce7f3,stroke:#9d174d,stroke-width:1.5px,color:#831843
```

**Reading it:** the fan-in to `Core` shows how small the library's public surface must stay — every test project and adapter couples to it. Integration tests (pink) are separated from unit tests (yellow) because they depend on Docker.

---

## 7. CI Pipeline

The GitHub Actions workflow at `.github/workflows/ci.yml`.

```mermaid
flowchart LR
    Start([push / PR]):::trigger --> Build["Build<br/>dotnet restore + build"]:::step
    Build --> Unit["Unit Tests"]:::step
    Build --> Acc["Acceptance Tests"]:::step
    Build --> E2E["End-to-End Tests"]:::step
    Build --> Docker{{"Docker-based<br/>Integration Tests"}}:::group
    Docker --> ESDB["EventStoreDB<br/>(TestContainers)"]:::docker
    Docker --> PG["PostgreSQL<br/>(postgres:16-alpine)"]:::docker
    Docker --> RMQ["RabbitMQ<br/>(rabbitmq:3-management)"]:::docker
    Docker --> RM["Read Model<br/>(postgres:16-alpine)"]:::docker

    Unit --> Artifacts[["Upload .trx artifacts<br/>(30-day retention)"]]:::sink
    Acc --> Artifacts
    E2E --> Artifacts
    ESDB --> Artifacts
    PG --> Artifacts
    RMQ --> Artifacts
    RM --> Artifacts

    classDef trigger fill:#dcfce7,stroke:#166534,stroke-width:2px,color:#064e3b
    classDef step fill:#cffafe,stroke:#155e75,stroke-width:1.5px,color:#164e63
    classDef group fill:#e0e7ff,stroke:#3730a3,stroke-width:2px,color:#312e81
    classDef docker fill:#fed7aa,stroke:#9a3412,stroke-width:1.5px,color:#7c2d12
    classDef sink fill:#fef3c7,stroke:#92400e,stroke-width:2px,color:#78350f
```

**Reading it:** Docker tests are parallelizable because each TestContainers fixture is isolated per test project. Artifact upload is a fan-in sink used for post-run inspection.

---

## Updating these diagrams

When any of the following change, this document should be refreshed:

- Public interfaces in `src/BullOak.Repositories/` — affects diagrams 2 and 3.
- New persistence adapter added — affects diagrams 1 and 6.
- Session load/save behaviour changes (e.g., batching, new hook points) — affects diagrams 4 and 5.
- CI workflow in `.github/workflows/ci.yml` gains or drops jobs — affects diagram 7.

Prefer updating this file in the same PR as the code change so the diagrams do not drift. If you change the colour palette, update the **Colour legend** section at the top so every diagram still speaks the same visual language.
