using System.Text.Json;
using BullOak.Console.Domain;
using BullOak.Console.Infrastructure;
using BullOak.Repositories;
using BullOak.Repositories.Appliers;
using BullOak.Repositories.Config;
using EventStore.Client;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 16: EventStoreDB Persistence
///
/// This demo uses a REAL EventStoreDB instance (via TestContainers or
/// a configured connection string) to persist and read events. It shows:
///
///   - Writing events to EventStoreDB streams (AppendToStreamAsync)
///   - Reading events back (ReadStreamAsync)
///   - Converting EventStoreDB events to BullOak StoredEvents
///   - Rehydrating BullOak state from EventStoreDB events
///   - Stream metadata and optimistic concurrency (StreamRevision)
///   - Reading events across all streams ($all)
///
/// Note: There is no dedicated BullOak.Repositories.EventStore adapter.
/// This demo shows the integration pattern manually — which is exactly
/// what a production adapter would encapsulate.
/// </summary>
public static class EventStorePersistenceDemo
{
    public static async Task RunAsync(InfrastructureManager infra)
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 16: EventStoreDB Persistence (Real Database)[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            $"[bold]Mode:[/] [aqua]{infra.GetEventStoreMode()}[/]\n\n" +
            "[dim]EventStoreDB is a purpose-built database for event sourcing.\n" +
            "Events are stored in streams, each identified by a string name.\n" +
            "Unlike PostgreSQL, EventStoreDB has built-in support for:\n" +
            "  - Stream subscriptions (real-time event push)\n" +
            "  - System projections (automatic cross-stream indexing)\n" +
            "  - Optimistic concurrency (stream revision checking)\n" +
            "  - Global event ordering ($all stream)[/]")
            .Header("[yellow]EventStoreDB Event Store[/]")
            .Border(BoxBorder.Rounded));

        // ── Get the EventStore client (starts container if needed) ──
        var (client, connectionString) = await infra.GetEventStoreClientAsync();
        AnsiConsole.MarkupLine($"[dim]Connection: {connectionString}[/]");
        AnsiConsole.WriteLine();

        // ── Configure BullOak (for rehydration) ────────────────
        var config = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var streamId = $"account-{Guid.NewGuid():N}";

        // ── Step 1: Write events to EventStoreDB ───────────────
        AnsiConsole.Write(new Panel(
            "[bold]var eventData = new EventData([/]\n" +
            "[bold]    Uuid.NewUuid(),[/]\n" +
            "[bold]    typeof(T).Name,                         [dim]// event type as string[/][/]\n" +
            "[bold]    JsonSerializer.SerializeToUtf8Bytes(e)); [dim]// JSON payload[/][/]\n\n" +
            "[bold]await client.AppendToStreamAsync(streamId, StreamState.NoStream, events);[/]\n\n" +
            "[dim]Events are serialized to UTF-8 JSON and appended to a named stream.\n" +
            "StreamState.NoStream = expect the stream doesn't exist yet.\n" +
            "StreamRevision.FromInt64(n) = expect stream at specific revision.[/]")
            .Header("[yellow]Step 1: Write Events to EventStoreDB[/]")
            .Border(BoxBorder.Rounded));

        var events = new object[]
        {
            new AccountOpened("Alice (EventStoreDB)", 5000m),
            new MoneyDeposited(1500m, "Salary"),
            new MoneyWithdrawn(200m, "Groceries"),
            new MoneyDeposited(3000m, "Bonus"),
        };

        var eventDatas = events.Select(e => new EventData(
            Uuid.NewUuid(),
            e.GetType().Name,
            JsonSerializer.SerializeToUtf8Bytes(e, e.GetType())
        )).ToArray();

        var writeResult = await client.AppendToStreamAsync(
            streamId,
            StreamState.NoStream,
            eventDatas);

        AnsiConsole.MarkupLine($"  Stream: [aqua]{streamId}[/]");
        AnsiConsole.MarkupLine($"  Events written: [green]{events.Length}[/]");
        AnsiConsole.MarkupLine($"  Next expected revision: [aqua]{writeResult.NextExpectedStreamRevision}[/]");
        AnsiConsole.WriteLine();

        // ── Step 2: Read events back from EventStoreDB ─────────
        AnsiConsole.Write(new Panel(
            "[bold]var readResult = client.ReadStreamAsync([/]\n" +
            "[bold]    Direction.Forwards,    [dim]// oldest first[/][/]\n" +
            "[bold]    streamId,[/]\n" +
            "[bold]    StreamPosition.Start); [dim]// from the beginning[/][/]\n\n" +
            "[dim]Returns an IAsyncEnumerable<ResolvedEvent>.\n" +
            "Each ResolvedEvent contains:\n" +
            "  - .Event.EventType (string)\n" +
            "  - .Event.Data (ReadOnlyMemory<byte> — the JSON payload)\n" +
            "  - .Event.EventNumber (position in the stream)\n" +
            "  - .Event.Created (UTC timestamp)[/]")
            .Header("[yellow]Step 2: Read Events from EventStoreDB[/]")
            .Border(BoxBorder.Rounded));

        var readResult = client.ReadStreamAsync(
            Direction.Forwards,
            streamId,
            StreamPosition.Start);

        var readEvents = new List<(string Type, object Event, ulong Position, DateTime Created)>();

        await foreach (var resolved in readResult)
        {
            var eventType = resolved.Event.EventType;
            var eventObj = DeserializeEvent(eventType, resolved.Event.Data.Span);
            readEvents.Add((eventType, eventObj, resolved.Event.EventNumber, resolved.Event.Created));
        }

        var eventTable = new Table().Border(TableBorder.Rounded);
        eventTable.AddColumn("[bold]#[/]");
        eventTable.AddColumn("[bold]Type[/]");
        eventTable.AddColumn("[bold]Data[/]");
        eventTable.AddColumn("[bold]Position[/]");

        foreach (var (type, evt, pos, created) in readEvents)
        {
            eventTable.AddRow(
                pos.ToString(),
                $"[aqua]{type}[/]",
                Markup.Escape(evt.ToString() ?? ""),
                pos.ToString());
        }

        AnsiConsole.Write(new Panel(eventTable)
            .Header("[aqua]Events read from EventStoreDB[/]")
            .Border(BoxBorder.Rounded));

        // ── Step 3: Rehydrate BullOak state from EventStoreDB ──
        AnsiConsole.Write(new Panel(
            "[bold]// Convert EventStoreDB events to BullOak StoredEvent[/]\n" +
            "[bold]var storedEvents = readEvents.Select((e, i) =>[/]\n" +
            "[bold]    new StoredEvent(e.GetType(), e, i)).ToArray();[/]\n\n" +
            "[bold]// Use BullOak's rehydrator to reconstruct state[/]\n" +
            "[bold]var result = config.StateRehydrator[/]\n" +
            "[bold]    .RehydrateFrom[[BankAccountState]](storedEvents);[/]\n\n" +
            "[dim]This is the bridge between EventStoreDB and BullOak.\n" +
            "Events read from EventStoreDB are converted to StoredEvent arrays\n" +
            "and fed into BullOak's rehydration pipeline (which applies\n" +
            "upconversion and event appliers).[/]")
            .Header("[yellow]Step 3: Rehydrate BullOak State[/]")
            .Border(BoxBorder.Rounded));

        var storedEvents = readEvents
            .Select((e, i) => new StoredEvent(e.Event.GetType(), e.Event, i))
            .ToArray();

        var rehydrateResult = config.StateRehydrator
            .RehydrateFrom<BankAccountState>(storedEvents);

        var state = rehydrateResult.State;

        var stateTable = new Table().Border(TableBorder.Rounded);
        stateTable.AddColumn("[bold]Property[/]");
        stateTable.AddColumn("[bold]Value[/]");
        stateTable.AddRow("AccountHolder", state.AccountHolder);
        stateTable.AddRow("Balance", $"[green]{state.Balance:C}[/]");
        stateTable.AddRow("TransactionCount", state.TransactionCount.ToString());
        stateTable.AddRow("LastEventIndex", rehydrateResult.LastEventIndex?.ToString() ?? "null");
        AnsiConsole.Write(new Panel(stateTable)
            .Header("[aqua]State rehydrated from EventStoreDB[/]")
            .Border(BoxBorder.Rounded));

        // ── Step 4: Append more events (with revision check) ───
        AnsiConsole.Write(new Panel(
            "[bold]// Optimistic concurrency: specify expected revision[/]\n" +
            "[bold]await client.AppendToStreamAsync([/]\n" +
            "[bold]    streamId,[/]\n" +
            "[bold]    StreamRevision.FromInt64(3), [dim]// expect 4 events (0-3)[/][/]\n" +
            "[bold]    newEvents);[/]\n\n" +
            "[dim]If another process appended events since we read,\n" +
            "WrongExpectedVersionException is thrown.[/]")
            .Header("[yellow]Step 4: Append with Concurrency Check[/]")
            .Border(BoxBorder.Rounded));

        var newEvents = new[]
        {
            new EventData(Uuid.NewUuid(), nameof(MoneyWithdrawn),
                JsonSerializer.SerializeToUtf8Bytes(new MoneyWithdrawn(750m, "Rent"))),
            new EventData(Uuid.NewUuid(), nameof(MoneyDeposited),
                JsonSerializer.SerializeToUtf8Bytes(new MoneyDeposited(100m, "Cashback"))),
        };

        var appendResult = await client.AppendToStreamAsync(
            streamId,
            StreamRevision.FromInt64(3), // We know there are 4 events (positions 0-3)
            newEvents);

        AnsiConsole.MarkupLine($"  Appended 2 more events. Next revision: [aqua]{appendResult.NextExpectedStreamRevision}[/]");
        AnsiConsole.WriteLine();

        // ── Step 5: Re-read and rehydrate with all events ──────
        AnsiConsole.MarkupLine("[yellow]Step 5:[/] Re-read all events and rehydrate...");

        var allEvents = new List<StoredEvent>();
        var reRead = client.ReadStreamAsync(Direction.Forwards, streamId, StreamPosition.Start);
        var idx = 0;
        await foreach (var resolved in reRead)
        {
            var eventObj = DeserializeEvent(resolved.Event.EventType, resolved.Event.Data.Span);
            allEvents.Add(new StoredEvent(eventObj.GetType(), eventObj, idx++));
        }

        var finalResult = config.StateRehydrator.RehydrateFrom<BankAccountState>(allEvents.ToArray());
        var finalState = finalResult.State;

        var finalTable = new Table().Border(TableBorder.Rounded);
        finalTable.AddColumn("[bold]Property[/]");
        finalTable.AddColumn("[bold]Value[/]");
        finalTable.AddRow("AccountHolder", finalState.AccountHolder);
        finalTable.AddRow("Balance", $"[green]{finalState.Balance:C}[/]");
        finalTable.AddRow("TransactionCount", finalState.TransactionCount.ToString());
        finalTable.AddRow("Total events in stream", allEvents.Count.ToString());
        AnsiConsole.Write(new Panel(finalTable)
            .Header("[aqua]Final state — all 6 events from EventStoreDB[/]")
            .Border(BoxBorder.Rounded));

        // ── Step 6: Read backwards (latest events first) ───────
        AnsiConsole.MarkupLine("[yellow]Step 6:[/] Read stream backwards (latest first)...");

        var backwardRead = client.ReadStreamAsync(
            Direction.Backwards,
            streamId,
            StreamPosition.End,
            maxCount: 3);

        var backTable = new Table().Border(TableBorder.Rounded);
        backTable.AddColumn("[bold]Position[/]");
        backTable.AddColumn("[bold]Type[/]");
        backTable.AddColumn("[bold]Data[/]");

        await foreach (var resolved in backwardRead)
        {
            var eventObj = DeserializeEvent(resolved.Event.EventType, resolved.Event.Data.Span);
            backTable.AddRow(
                resolved.Event.EventNumber.ToString(),
                $"[aqua]{resolved.Event.EventType}[/]",
                Markup.Escape(eventObj.ToString() ?? ""));
        }

        AnsiConsole.Write(new Panel(backTable)
            .Header("[aqua]Last 3 events (backwards)[/]")
            .Border(BoxBorder.Rounded));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]All EventStoreDB persistence operations completed successfully.[/]");
    }

    /// <summary>
    /// Deserializes an event from its EventStoreDB representation.
    /// In production, use a type registry or convention-based mapper.
    /// </summary>
    private static object DeserializeEvent(string eventType, ReadOnlySpan<byte> data)
    {
        return eventType switch
        {
            nameof(AccountOpened) => JsonSerializer.Deserialize<AccountOpened>(data)!,
            nameof(MoneyDeposited) => JsonSerializer.Deserialize<MoneyDeposited>(data)!,
            nameof(MoneyWithdrawn) => JsonSerializer.Deserialize<MoneyWithdrawn>(data)!,
            nameof(AccountFrozen) => JsonSerializer.Deserialize<AccountFrozen>(data)!,
            nameof(AccountUnfrozen) => JsonSerializer.Deserialize<AccountUnfrozen>(data)!,
            _ => throw new InvalidOperationException($"Unknown event type: {eventType}")
        };
    }
}
