using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 10: Point-in-Time Queries (appliesAt)
///
/// BullOak supports temporal queries: load the state as it was at a
/// specific point in time. This is done by passing an appliesAt DateTime
/// to BeginSessionFor. Only events created on or before that timestamp
/// are included in rehydration.
///
/// Use cases:
///   - Audit trails ("What was the balance on Dec 31?")
///   - Debugging ("What was the state before that bug?")
///   - Regulatory compliance
///   - Historical reporting
/// </summary>
public static class PointInTimeQueryDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 10: Point-in-Time Queries (appliesAt)[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            "[bold]await repo.BeginSessionFor(id, throwIfNotExists: true, appliesAt: someDate)[/]\n\n" +
            "[dim]Only events with created_at <= appliesAt are loaded.\n" +
            "The state is reconstructed as it was at that moment.\n" +
            "The InMemory repository stores timestamps with each event.\n" +
            "PostgreSQL uses the created_at column for filtering.[/]")
            .Header("[yellow]How appliesAt Works[/]")
            .Border(BoxBorder.Rounded));

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
        var accountId = "TIME-001";

        // Create events at different "times" (simulated by saving in sequence)
        var baseTime = DateTime.UtcNow;

        // Day 1: Account opened
        using (var session = await repo.BeginSessionFor(accountId, throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Time Traveler", 1000m));
            await session.SaveChanges();
        }
        AnsiConsole.MarkupLine("[dim]Day 1: Account opened with 1000[/]");

        // Day 2: Salary deposit
        await Task.Delay(50); // Small delay to ensure different timestamps
        using (var session = await repo.BeginSessionFor(accountId, throwIfNotExists: true))
        {
            session.AddEvent(new MoneyDeposited(2000m, "Salary"));
            await session.SaveChanges();
        }
        var afterSalary = DateTime.UtcNow;
        AnsiConsole.MarkupLine("[dim]Day 2: Salary deposit of 2000[/]");

        // Day 3: Rent payment
        await Task.Delay(50);
        using (var session = await repo.BeginSessionFor(accountId, throwIfNotExists: true))
        {
            session.AddEvent(new MoneyWithdrawn(800m, "Rent"));
            await session.SaveChanges();
        }
        AnsiConsole.MarkupLine("[dim]Day 3: Rent payment of 800[/]");

        // Day 4: Shopping
        await Task.Delay(50);
        using (var session = await repo.BeginSessionFor(accountId, throwIfNotExists: true))
        {
            session.AddEvent(new MoneyWithdrawn(150m, "Shopping"));
            await session.SaveChanges();
        }
        AnsiConsole.MarkupLine("[dim]Day 4: Shopping expense of 150[/]");
        AnsiConsole.WriteLine();

        // ── Query current state ────────────────────────────────
        using var currentSession = await repo.BeginSessionFor(accountId, throwIfNotExists: true);
        var currentState = currentSession.GetCurrentState();

        // ── Query as of after salary (before rent) ─────────────
        using var historicalSession = await repo.BeginSessionFor(accountId, throwIfNotExists: true, appliesAt: afterSalary);
        var historicalState = historicalSession.GetCurrentState();

        // Show comparison
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Query[/]");
        table.AddColumn("[bold]Balance[/]");
        table.AddColumn("[bold]Transactions[/]");
        table.AddRow(
            "Current (all events)",
            $"[green]{currentState.Balance:C}[/]",
            currentState.TransactionCount.ToString());
        table.AddRow(
            $"As of {afterSalary:HH:mm:ss.fff} (before rent)",
            $"[yellow]{historicalState.Balance:C}[/]",
            historicalState.TransactionCount.ToString());
        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            "[dim]Timeline of events:\n" +
            "  Day 1: +1000 (opened)     → Balance: 1000\n" +
            "  Day 2: +2000 (salary)     → Balance: 3000  ← appliesAt snapshot here\n" +
            "  Day 3:  -800 (rent)       → Balance: 2200\n" +
            "  Day 4:  -150 (shopping)   → Balance: 2050  ← current state\n\n" +
            "The point-in-time query loaded only events up to 'afterSalary',\n" +
            "giving us the balance as it was before rent and shopping.\n\n" +
            "This is powerful for auditing, compliance, and debugging.[/]")
            .Header("[yellow]Timeline Visualization[/]")
            .Border(BoxBorder.Rounded));
    }
}
