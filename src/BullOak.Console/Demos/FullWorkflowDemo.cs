using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 14: Full Interactive Workflow
///
/// An interactive bank account simulation combining all BullOak features:
///   - Configuration with all options
///   - Event sourcing lifecycle
///   - State tracking
///   - Multiple accounts
///   - Event history visualization
///
/// This is the "everything together" demo showing how BullOak is used
/// in a real application workflow.
/// </summary>
public static class FullWorkflowDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 14: Full Interactive Bank Workflow[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var publishedEvents = new List<string>();
        var metricsInterceptor = new MetricsInterceptor();

        // Full-featured configuration
        var config = Configuration.Begin()
            .WithDefaultCollection()
            .WithInterceptor(metricsInterceptor)
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithEventPublisher(item =>
            {
                publishedEvents.Add($"{item.type.Name}: {item.instance}");
            })
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var validator = new BankAccountValidator();
        var repo = new InMemoryEventSourcedRepository<string, BankAccountState>(validator, config);

        // ── Create multiple accounts ───────────────────────────
        AnsiConsole.MarkupLine("[yellow]Creating bank accounts...[/]");

        var accounts = new[]
        {
            ("ACC-ALICE", "Alice Smith", 5000m),
            ("ACC-BOB", "Bob Johnson", 3000m),
            ("ACC-CAROL", "Carol White", 8000m),
        };

        foreach (var (id, name, deposit) in accounts)
        {
            using var session = await repo.BeginSessionFor(id, throwIfNotExists: false);
            session.AddEvent(new AccountOpened(name, deposit));
            await session.SaveChanges();
        }

        // ── Perform transactions ───────────────────────────────
        AnsiConsole.MarkupLine("[yellow]Processing transactions...[/]");
        AnsiConsole.WriteLine();

        // Alice: salary + shopping
        using (var session = await repo.BeginSessionFor("ACC-ALICE", throwIfNotExists: true))
        {
            session.AddEvent(new MoneyDeposited(2500m, "Monthly salary"));
            session.AddEvent(new MoneyWithdrawn(150m, "Grocery shopping"));
            session.AddEvent(new MoneyWithdrawn(89.99m, "Online purchase"));
            await session.SaveChanges();
        }

        // Bob: freelance work
        using (var session = await repo.BeginSessionFor("ACC-BOB", throwIfNotExists: true))
        {
            session.AddEvent(new MoneyDeposited(1200m, "Freelance project"));
            session.AddEvent(new MoneyWithdrawn(500m, "Rent"));
            await session.SaveChanges();
        }

        // Carol: various transactions
        using (var session = await repo.BeginSessionFor("ACC-CAROL", throwIfNotExists: true))
        {
            session.AddEvent(new MoneyDeposited(3000m, "Bonus"));
            session.AddEvent(new MoneyWithdrawn(2000m, "Car payment"));
            session.AddEvent(new MoneyWithdrawn(250m, "Utilities"));
            session.AddEvent(new MoneyDeposited(100m, "Cashback"));
            await session.SaveChanges();
        }

        // ── Display all account states ─────────────────────────
        var accountTable = new Table().Border(TableBorder.Double);
        accountTable.AddColumn("[bold]Account ID[/]");
        accountTable.AddColumn("[bold]Holder[/]");
        accountTable.AddColumn("[bold]Balance[/]");
        accountTable.AddColumn("[bold]Transactions[/]");
        accountTable.AddColumn("[bold]Frozen[/]");

        foreach (var (id, _, _) in accounts)
        {
            using var session = await repo.BeginSessionFor(id, throwIfNotExists: true);
            var state = session.GetCurrentState();
            accountTable.AddRow(
                id,
                state.AccountHolder,
                $"[green]{state.Balance:C}[/]",
                state.TransactionCount.ToString(),
                state.IsFrozen ? "[red]Yes[/]" : "[green]No[/]");
        }

        AnsiConsole.Write(new Panel(accountTable)
            .Header("[yellow]Account Summary[/]")
            .Border(BoxBorder.Rounded));

        // ── Freeze an account ──────────────────────────────────
        AnsiConsole.MarkupLine("[yellow]Freezing Bob's account (fraud detected)...[/]");
        using (var session = await repo.BeginSessionFor("ACC-BOB", throwIfNotExists: true))
        {
            session.AddEvent(new AccountFrozen("Suspicious transaction detected"));
            await session.SaveChanges();
        }

        using (var session = await repo.BeginSessionFor("ACC-BOB", throwIfNotExists: true))
        {
            var state = session.GetCurrentState();
            AnsiConsole.MarkupLine($"  Bob's account frozen: [red]{state.IsFrozen}[/]");
        }
        AnsiConsole.WriteLine();

        // ── Published events log ───────────────────────────────
        var eventTable = new Table().Border(TableBorder.Rounded);
        eventTable.AddColumn("[bold]#[/]");
        eventTable.AddColumn("[bold]Published Event[/]");
        for (int i = 0; i < publishedEvents.Count; i++)
            eventTable.AddRow((i + 1).ToString(), publishedEvents[i]);

        AnsiConsole.Write(new Panel(eventTable)
            .Header("[yellow]Event Publishing Log[/]")
            .Border(BoxBorder.Rounded));

        // ── Metrics ────────────────────────────────────────────
        var metricsTable = new Table().Border(TableBorder.Rounded);
        metricsTable.AddColumn("[bold]Metric[/]");
        metricsTable.AddColumn("[bold]Value[/]");
        metricsTable.AddRow("Total streams", repo.IdsOfStreamsWithEvents.Length.ToString());
        metricsTable.AddRow("Events published", metricsInterceptor.PublishCount.ToString());
        metricsTable.AddRow("Events saved", metricsInterceptor.SaveCount.ToString());
        metricsTable.AddRow("Published events list", publishedEvents.Count.ToString());

        AnsiConsole.Write(new Panel(metricsTable)
            .Header("[yellow]System Metrics (from interceptors)[/]")
            .Border(BoxBorder.Rounded));

        // ── Delete an account ──────────────────────────────────
        AnsiConsole.MarkupLine("[yellow]Closing Carol's account (deleting all events)...[/]");
        await repo.Delete("ACC-CAROL");
        AnsiConsole.MarkupLine($"  Contains(\"ACC-CAROL\"): [red]{await repo.Contains("ACC-CAROL")}[/]");
        AnsiConsole.MarkupLine($"  Remaining streams: [aqua]{string.Join(", ", repo.IdsOfStreamsWithEvents)}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Full workflow complete — all BullOak features working together.[/]");
    }
}
