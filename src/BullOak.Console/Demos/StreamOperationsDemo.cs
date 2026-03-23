using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.Exceptions;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 11: Stream Operations (Contains, Delete, throwIfNotExists)
///
/// Repository-level operations for managing event streams:
///   - Contains(id): Check if a stream has events
///   - Delete(id): Remove all events for a stream
///   - throwIfNotExists: Control behavior for missing streams
///   - StreamNotFoundException: Thrown when stream doesn't exist
/// </summary>
public static class StreamOperationsDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 11: Stream Operations[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var config = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var repo = new InMemoryEventSourcedRepository<string, BankAccountState>(config);

        // ── Contains ───────────────────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold]bool exists = await repo.Contains(\"ACC-001\");[/]\n\n" +
            "[dim]Returns true if the stream has at least one event.\n" +
            "Returns false for non-existent or empty streams.[/]")
            .Header("[yellow]Contains(id)[/]")
            .Border(BoxBorder.Rounded));

        AnsiConsole.MarkupLine($"  Before adding events: Contains(\"STREAM-001\") = [aqua]{await repo.Contains("STREAM-001")}[/]");

        using (var session = await repo.BeginSessionFor("STREAM-001", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Stream User", 500m));
            await session.SaveChanges();
        }

        AnsiConsole.MarkupLine($"  After adding events:  Contains(\"STREAM-001\") = [green]{await repo.Contains("STREAM-001")}[/]");
        AnsiConsole.WriteLine();

        // ── throwIfNotExists ───────────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold]// throwIfNotExists: false (default) — creates empty stream if missing[/]\n" +
            "[bold]await repo.BeginSessionFor(\"NEW-ID\", throwIfNotExists: false);[/]\n\n" +
            "[bold]// throwIfNotExists: true — throws StreamNotFoundException[/]\n" +
            "[bold]await repo.BeginSessionFor(\"MISSING\", throwIfNotExists: true);[/]\n\n" +
            "[dim]Use false for create-or-update scenarios.\n" +
            "Use true when you expect the entity to already exist.[/]")
            .Header("[yellow]throwIfNotExists Parameter[/]")
            .Border(BoxBorder.Rounded));

        // Demonstrate false (creates new)
        using (var session = await repo.BeginSessionFor("NEW-STREAM", throwIfNotExists: false))
        {
            AnsiConsole.MarkupLine($"  throwIfNotExists=false: IsNewState = [green]{session.IsNewState}[/] (new empty stream created)");
        }

        // Demonstrate true (throws)
        try
        {
            using var session = await repo.BeginSessionFor("NONEXISTENT", throwIfNotExists: true);
            AnsiConsole.MarkupLine("[red]  ERROR: Should have thrown![/]");
        }
        catch (StreamNotFoundException ex)
        {
            AnsiConsole.MarkupLine($"  throwIfNotExists=true:  [green]StreamNotFoundException caught[/]");
            AnsiConsole.MarkupLine($"  [dim]{ex.Message}[/]");
        }
        AnsiConsole.WriteLine();

        // ── Delete ─────────────────────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold]await repo.Delete(\"STREAM-001\");[/]\n\n" +
            "[dim]Removes ALL events for the given stream.\n" +
            "After deletion, Contains() returns false.\n" +
            "The stream can be recreated by adding new events.[/]")
            .Header("[yellow]Delete(id)[/]")
            .Border(BoxBorder.Rounded));

        AnsiConsole.MarkupLine($"  Before delete: Contains(\"STREAM-001\") = [green]{await repo.Contains("STREAM-001")}[/]");
        await repo.Delete("STREAM-001");
        AnsiConsole.MarkupLine($"  After delete:  Contains(\"STREAM-001\") = [red]{await repo.Contains("STREAM-001")}[/]");
        AnsiConsole.WriteLine();

        // ── AddEvent overloads ─────────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold]// Single event (object)[/]\n" +
            "[bold]session.AddEvent(new MoneyDeposited(100m, \"Salary\"));[/]\n\n" +
            "[bold]// Typed factory — BullOak creates the instance, you initialize it[/]\n" +
            "[bold]session.AddEvent<MoneyDeposited>(e => { /* init e */ });[/]\n\n" +
            "[bold]// Multiple events (array)[/]\n" +
            "[bold]session.AddEvents(new object[] { event1, event2 });[/]\n\n" +
            "[bold]// Multiple events (IEnumerable)[/]\n" +
            "[bold]session.AddEvents(eventList);[/]")
            .Header("[yellow]AddEvent / AddEvents Overloads[/]")
            .Border(BoxBorder.Rounded));

        // Demonstrate AddEvents with array
        using (var session = await repo.BeginSessionFor("MULTI-001", throwIfNotExists: false))
        {
            // Array overload
            session.AddEvents(new object[]
            {
                new AccountOpened("Multi User", 100m),
                new MoneyDeposited(200m, "First"),
                new MoneyDeposited(300m, "Second"),
                new MoneyWithdrawn(50m, "Small purchase")
            });

            var state = session.GetCurrentState();
            AnsiConsole.MarkupLine($"  AddEvents (array of 4): Balance = [green]{state.Balance:C}[/], Transactions = {state.TransactionCount}");
            await session.SaveChanges();
        }

        // Demonstrate AddEvents with IEnumerable
        using (var session = await repo.BeginSessionFor("ENUM-001", throwIfNotExists: false))
        {
            var events = new List<object>
            {
                new AccountOpened("Enum User", 1000m),
                new MoneyDeposited(500m, "Enumerable deposit")
            };
            session.AddEvents(events);

            var state = session.GetCurrentState();
            AnsiConsole.MarkupLine($"  AddEvents (IEnumerable): Balance = [green]{state.Balance:C}[/], Transactions = {state.TransactionCount}");
            await session.SaveChanges();
        }

        // ── IdsOfStreamsWithEvents (InMemory only) ─────────────
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]InMemory-specific: IdsOfStreamsWithEvents[/]");
        var ids = repo.IdsOfStreamsWithEvents;
        var idTable = new Table().Border(TableBorder.Rounded);
        idTable.AddColumn("[bold]Stream IDs with Events[/]");
        foreach (var id in ids)
            idTable.AddRow(id);
        AnsiConsole.Write(idTable);
    }
}
