using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 1: Basic Event Sourcing
///
/// This is the "Hello World" of BullOak. It shows the fundamental workflow:
///   1. Configure BullOak (event collection, state factory, appliers, etc.)
///   2. Create a repository (in-memory for this demo)
///   3. Begin a session for an entity (by its ID)
///   4. Add events to the session
///   5. Inspect the state (it updates automatically as events are added)
///   6. Save the session (persists events to the store)
///
/// Key concepts demonstrated:
///   - Configuration.Begin() fluent builder
///   - WithDefaultCollection()
///   - WithDefaultStateFactory()
///   - NeverUseThreadSafe() / AlwaysUseThreadSafe()
///   - WithNoEventPublisher()
///   - WithAnyAppliersFrom(assembly)    ← auto-discovery via reflection
///   - WithNoUpconverters()
///   - InMemoryEventSourcedRepository
///   - session.IsNewState
///   - session.AddEvent()
///   - session.GetCurrentState()
///   - session.SaveChanges()
/// </summary>
public static class BasicEventSourcingDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 1: Basic Event Sourcing[/]").LeftJustified());
        AnsiConsole.WriteLine();

        // ── Step 1: Configure BullOak ──────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold]Configuration.Begin()[/]\n" +
            "  .WithDefaultCollection()        [dim]// Use default linked list for event collection[/]\n" +
            "  .WithDefaultStateFactory()       [dim]// Use BullOak's built-in state factory[/]\n" +
            "  .NeverUseThreadSafe()            [dim]// No thread-safe locking (single-threaded demo)[/]\n" +
            "  .WithNoEventPublisher()          [dim]// No event bus / message broker[/]\n" +
            "  .WithAnyAppliersFrom(assembly)   [dim]// Auto-discover IApplyEvent<> implementations[/]\n" +
            "  .AndNoMoreAppliers()             [dim]// Signal that all appliers are registered[/]\n" +
            "  .WithNoUpconverters()            [dim]// No event schema versioning[/]\n" +
            "  .Build()                         [dim]// Returns IHoldAllConfiguration[/]")
            .Header("[yellow]Step 1: Configure BullOak[/]")
            .Border(BoxBorder.Rounded));

        var configuration = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        AnsiConsole.MarkupLine("[green]Configuration built successfully.[/]");
        AnsiConsole.WriteLine();

        // ── Step 2: Create a repository ────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold]new InMemoryEventSourcedRepository<string, BankAccountState>(configuration)[/]\n\n" +
            "[dim]The repository implements IStartSessions<TId, TState>.\n" +
            "It provides three methods:\n" +
            "  - BeginSessionFor(id, throwIfNotExists, appliesAt?)\n" +
            "  - Contains(id)\n" +
            "  - Delete(id)[/]")
            .Header("[yellow]Step 2: Create Repository[/]")
            .Border(BoxBorder.Rounded));

        var repo = new InMemoryEventSourcedRepository<string, BankAccountState>(configuration);
        AnsiConsole.MarkupLine("[green]InMemoryEventSourcedRepository created.[/]");
        AnsiConsole.WriteLine();

        // ── Step 3: Open a session and add events ──────────────
        var accountId = "ACC-001";

        AnsiConsole.Write(new Panel(
            $"[bold]using var session = await repo.BeginSessionFor(\"{accountId}\", throwIfNotExists: false);[/]\n\n" +
            "[dim]When throwIfNotExists is false, a new empty stream is created.\n" +
            "When true, StreamNotFoundException is thrown if the stream doesn't exist.\n" +
            "The session loads all existing events and rehydrates the state.[/]")
            .Header("[yellow]Step 3: Begin Session[/]")
            .Border(BoxBorder.Rounded));

        using var session = await repo.BeginSessionFor(accountId, throwIfNotExists: false);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");
        table.AddRow("session.IsNewState", session.IsNewState.ToString());
        table.AddRow("Initial Balance", session.GetCurrentState().Balance.ToString("C"));
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // ── Step 4: Add events ─────────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold]session.AddEvent(new AccountOpened(\"Alice Smith\", 1000m));[/]\n" +
            "[bold]session.AddEvent(new MoneyDeposited(500m, \"Salary\"));[/]\n" +
            "[bold]session.AddEvent(new MoneyWithdrawn(200m, \"Rent\"));[/]\n" +
            "[bold]session.AddEvent(new MoneyDeposited(50m, \"Cashback\"));[/]\n\n" +
            "[dim]Each AddEvent call immediately applies the event to the in-memory state\n" +
            "via the registered event applier. The state is always up-to-date.[/]")
            .Header("[yellow]Step 4: Add Events[/]")
            .Border(BoxBorder.Rounded));

        session.AddEvent(new AccountOpened("Alice Smith", 1000m));
        AnsiConsole.MarkupLine($"  After AccountOpened:  Balance = [green]{session.GetCurrentState().Balance:C}[/]");

        session.AddEvent(new MoneyDeposited(500m, "Salary"));
        AnsiConsole.MarkupLine($"  After MoneyDeposited: Balance = [green]{session.GetCurrentState().Balance:C}[/]");

        session.AddEvent(new MoneyWithdrawn(200m, "Rent"));
        AnsiConsole.MarkupLine($"  After MoneyWithdrawn: Balance = [green]{session.GetCurrentState().Balance:C}[/]");

        session.AddEvent(new MoneyDeposited(50m, "Cashback"));
        AnsiConsole.MarkupLine($"  After MoneyDeposited: Balance = [green]{session.GetCurrentState().Balance:C}[/]");
        AnsiConsole.WriteLine();

        // ── Step 5: Inspect final state ────────────────────────
        var finalState = session.GetCurrentState();
        var stateTable = new Table().Border(TableBorder.Rounded);
        stateTable.AddColumn("[bold]Property[/]");
        stateTable.AddColumn("[bold]Value[/]");
        stateTable.AddRow("AccountHolder", finalState.AccountHolder);
        stateTable.AddRow("Balance", $"[green]{finalState.Balance:C}[/]");
        stateTable.AddRow("IsFrozen", finalState.IsFrozen.ToString());
        stateTable.AddRow("TransactionCount", finalState.TransactionCount.ToString());
        stateTable.AddRow("IsNewState", session.IsNewState.ToString());

        AnsiConsole.Write(new Panel(stateTable)
            .Header("[yellow]Step 5: Final State (before save)[/]")
            .Border(BoxBorder.Rounded));

        // ── Step 6: Save changes ───────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold]int eventsSaved = await session.SaveChanges();[/]\n\n" +
            "[dim]SaveChanges persists all new events to the store.\n" +
            "Returns the number of events that were saved.\n" +
            "After save, the session is still usable for reading state.[/]")
            .Header("[yellow]Step 6: Save Changes[/]")
            .Border(BoxBorder.Rounded));

        int saved = await session.SaveChanges();
        AnsiConsole.MarkupLine($"[green]Events saved: {saved}[/]");
        AnsiConsole.WriteLine();

        // ── Step 7: Verify persistence ─────────────────────────
        AnsiConsole.MarkupLine("[yellow]Step 7:[/] Verify by opening a new session for the same account...");
        using var session2 = await repo.BeginSessionFor(accountId, throwIfNotExists: true);
        var reloadedState = session2.GetCurrentState();

        var verifyTable = new Table().Border(TableBorder.Rounded);
        verifyTable.AddColumn("[bold]Property[/]");
        verifyTable.AddColumn("[bold]Value[/]");
        verifyTable.AddRow("AccountHolder", reloadedState.AccountHolder);
        verifyTable.AddRow("Balance", $"[green]{reloadedState.Balance:C}[/]");
        verifyTable.AddRow("TransactionCount", reloadedState.TransactionCount.ToString());
        verifyTable.AddRow("IsNewState", session2.IsNewState.ToString());
        AnsiConsole.Write(verifyTable);

        AnsiConsole.MarkupLine("[green]State was fully reconstructed from persisted events.[/]");
    }
}
