using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.Exceptions;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 9: Optimistic Concurrency
///
/// When two sessions load the same entity simultaneously, modify it,
/// and try to save — one will succeed and one will get a ConcurrencyException.
///
/// How it works:
///   - Each session records the last event index when it loaded
///   - On save, it checks if new events were added since that index
///   - If yes → ConcurrencyException (the stream was modified by someone else)
///   - The failed session should reload and retry
///
/// This prevents lost updates without pessimistic locking.
/// </summary>
public static class ConcurrencyDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 9: Optimistic Concurrency[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            "[dim]Optimistic concurrency prevents lost updates:\n\n" +
            "  Session A: Load(balance=1000) → Withdraw(200) → Save ✓\n" +
            "  Session B: Load(balance=1000) → Deposit(300)  → Save ✗ ConcurrencyException!\n\n" +
            "Session B loaded stale data (before Session A's withdrawal).\n" +
            "Without concurrency checking, Session B's save would overwrite\n" +
            "Session A's changes, losing the withdrawal.\n\n" +
            "BullOak tracks the expected next event position.\n" +
            "If another session saved events in between, the position\n" +
            "won't match and ConcurrencyException is thrown.[/]")
            .Header("[yellow]How Optimistic Concurrency Works[/]")
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

        // Setup: Create an account with initial balance
        var accountId = "CONC-001";
        using (var setup = await repo.BeginSessionFor(accountId, throwIfNotExists: false))
        {
            setup.AddEvent(new AccountOpened("Concurrent User", 1000m));
            await setup.SaveChanges();
        }
        AnsiConsole.MarkupLine("[dim]Setup: Created account with balance = 1000[/]");
        AnsiConsole.WriteLine();

        // ── Two sessions load simultaneously ───────────────────
        AnsiConsole.MarkupLine("[yellow]Both sessions load the same account (balance = 1000)...[/]");

        using var sessionA = await repo.BeginSessionFor(accountId, throwIfNotExists: true);
        using var sessionB = await repo.BeginSessionFor(accountId, throwIfNotExists: true);

        AnsiConsole.MarkupLine($"  Session A sees balance: [aqua]{sessionA.GetCurrentState().Balance:C}[/]");
        AnsiConsole.MarkupLine($"  Session B sees balance: [aqua]{sessionB.GetCurrentState().Balance:C}[/]");
        AnsiConsole.WriteLine();

        // Session A modifies and saves first
        AnsiConsole.MarkupLine("[yellow]Session A: Withdraw 200 and save...[/]");
        sessionA.AddEvent(new MoneyWithdrawn(200m, "Session A withdrawal"));
        await sessionA.SaveChanges();
        AnsiConsole.MarkupLine($"  Session A saved. New balance: [green]{sessionA.GetCurrentState().Balance:C}[/]");
        AnsiConsole.WriteLine();

        // Session B tries to save — should fail
        AnsiConsole.MarkupLine("[yellow]Session B: Deposit 300 and try to save...[/]");
        sessionB.AddEvent(new MoneyDeposited(300m, "Session B deposit"));

        try
        {
            await sessionB.SaveChanges();
            AnsiConsole.MarkupLine("[red]  ERROR: Should have thrown ConcurrencyException![/]");
        }
        catch (ConcurrencyException ex)
        {
            AnsiConsole.MarkupLine($"  [green]ConcurrencyException caught![/]");
            AnsiConsole.MarkupLine($"  [dim]{ex.Message}[/]");
            AnsiConsole.WriteLine();

            // Show retry pattern
            AnsiConsole.Write(new Panel(
                "[bold]Retry pattern:[/]\n\n" +
                "[bold]try { await session.SaveChanges(); }[/]\n" +
                "[bold]catch (ConcurrencyException)[/]\n" +
                "[bold]{[/]\n" +
                "[bold]    // Reload the session with fresh data[/]\n" +
                "[bold]    using var retry = await repo.BeginSessionFor(id, true);[/]\n" +
                "[bold]    // Re-apply business logic based on current state[/]\n" +
                "[bold]    retry.AddEvent(new MoneyDeposited(300m, \"Retry\"));[/]\n" +
                "[bold]    await retry.SaveChanges();[/]\n" +
                "[bold]}[/]")
                .Header("[yellow]Handling ConcurrencyException[/]")
                .Border(BoxBorder.Rounded));

            // Actually retry
            AnsiConsole.MarkupLine("[yellow]Retrying Session B with fresh data...[/]");
            using var retry = await repo.BeginSessionFor(accountId, throwIfNotExists: true);
            AnsiConsole.MarkupLine($"  Fresh balance: [aqua]{retry.GetCurrentState().Balance:C}[/]");
            retry.AddEvent(new MoneyDeposited(300m, "Session B deposit (retry)"));
            await retry.SaveChanges();
            AnsiConsole.MarkupLine($"  Final balance: [green]{retry.GetCurrentState().Balance:C}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Both transactions applied correctly without lost updates.[/]");
    }
}
