using BullOak.Console.Domain;
using BullOak.Console.Infrastructure;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.PostgreSql;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 15: PostgreSQL Persistence
///
/// This demo uses a REAL PostgreSQL database (via TestContainers or
/// a configured connection string) to persist events. It demonstrates:
///
///   - PostgreSqlEventStoreSchema.EnsureSchemaAsync() — idempotent table creation
///   - PostgreSqlEventSourcedRepository — full IStartSessions implementation
///   - Writing events that are persisted as JSONB in PostgreSQL
///   - Rehydrating state from persisted events (survives app restart)
///   - Contains(), Delete(), appliesAt queries against real data
///   - Optimistic concurrency via UNIQUE constraint on (stream_id, stream_position)
///
/// Events are stored in a table:
///   global_position (BIGSERIAL PK), stream_id, stream_position,
///   event_type, event_data (JSONB), created_at (TIMESTAMPTZ)
/// </summary>
public static class PostgreSqlPersistenceDemo
{
    public static async Task RunAsync(InfrastructureManager infra)
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 15: PostgreSQL Persistence (Real Database)[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            $"[bold]Mode:[/] [aqua]{infra.GetPostgreSqlMode()}[/]\n\n" +
            "[dim]Events are persisted as JSONB rows in PostgreSQL.\n" +
            "The schema uses a UNIQUE constraint on (stream_id, stream_position)\n" +
            "for optimistic concurrency. Dapper is used for all data access.\n" +
            "NpgsqlDataSource provides connection pooling.[/]")
            .Header("[yellow]PostgreSQL Event Store[/]")
            .Border(BoxBorder.Rounded));

        // ── Get the data source (starts container if needed) ───
        var dataSource = await infra.GetPostgreSqlDataSourceAsync();

        // ── Configure BullOak ──────────────────────────────────
        var config = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var repo = new PostgreSqlEventSourcedRepository<BankAccountState>(config, dataSource);

        AnsiConsole.MarkupLine("[green]PostgreSqlEventSourcedRepository created.[/]");
        AnsiConsole.WriteLine();

        // ── Step 1: Create accounts ────────────────────────────
        var aliceId = $"pg-alice-{Guid.NewGuid():N}";
        var bobId = $"pg-bob-{Guid.NewGuid():N}";

        AnsiConsole.Write(new Panel(
            "[bold]using var session = await repo.BeginSessionFor(streamId, throwIfNotExists: false);[/]\n" +
            "[bold]session.AddEvent(new AccountOpened(...));[/]\n" +
            "[bold]await session.SaveChanges();[/]\n\n" +
            "[dim]Events are written to PostgreSQL inside a transaction.\n" +
            "Each event gets a stream_position (0, 1, 2...) and global_position (auto-increment).[/]")
            .Header("[yellow]Step 1: Create Accounts[/]")
            .Border(BoxBorder.Rounded));

        using (var session = await repo.BeginSessionFor(aliceId, throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Alice (PostgreSQL)", 5000m));
            session.AddEvent(new MoneyDeposited(1500m, "Salary"));
            session.AddEvent(new MoneyWithdrawn(200m, "Groceries"));
            int saved = await session.SaveChanges();
            AnsiConsole.MarkupLine($"  Alice: [green]{saved} events persisted to PostgreSQL[/]");
        }

        using (var session = await repo.BeginSessionFor(bobId, throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Bob (PostgreSQL)", 3000m));
            session.AddEvent(new MoneyDeposited(800m, "Freelance"));
            int saved = await session.SaveChanges();
            AnsiConsole.MarkupLine($"  Bob:   [green]{saved} events persisted to PostgreSQL[/]");
        }
        AnsiConsole.WriteLine();

        // ── Step 2: Rehydrate from PostgreSQL ──────────────────
        AnsiConsole.Write(new Panel(
            "[dim]Opening a NEW session loads all events from PostgreSQL\n" +
            "and replays them through the appliers to reconstruct state.\n" +
            "This proves the data survived the round-trip to the database.[/]")
            .Header("[yellow]Step 2: Rehydrate State from PostgreSQL[/]")
            .Border(BoxBorder.Rounded));

        using (var session = await repo.BeginSessionFor(aliceId, throwIfNotExists: true))
        {
            var state = session.GetCurrentState();
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Property[/]");
            table.AddColumn("[bold]Value[/]");
            table.AddRow("AccountHolder", state.AccountHolder);
            table.AddRow("Balance", $"[green]{state.Balance:C}[/]");
            table.AddRow("TransactionCount", state.TransactionCount.ToString());
            table.AddRow("IsNewState", session.IsNewState.ToString());
            AnsiConsole.Write(new Panel(table).Header("[aqua]Alice — rehydrated from PostgreSQL[/]").Border(BoxBorder.Rounded));
        }

        using (var session = await repo.BeginSessionFor(bobId, throwIfNotExists: true))
        {
            var state = session.GetCurrentState();
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Property[/]");
            table.AddColumn("[bold]Value[/]");
            table.AddRow("AccountHolder", state.AccountHolder);
            table.AddRow("Balance", $"[green]{state.Balance:C}[/]");
            table.AddRow("TransactionCount", state.TransactionCount.ToString());
            AnsiConsole.Write(new Panel(table).Header("[aqua]Bob — rehydrated from PostgreSQL[/]").Border(BoxBorder.Rounded));
        }

        // ── Step 3: Add more events to existing stream ─────────
        AnsiConsole.MarkupLine("[yellow]Step 3:[/] Adding more transactions to Alice's account...");
        using (var session = await repo.BeginSessionFor(aliceId, throwIfNotExists: true))
        {
            session.AddEvent(new MoneyDeposited(3000m, "Bonus"));
            session.AddEvent(new MoneyWithdrawn(750m, "Rent"));
            await session.SaveChanges();
        }

        using (var session = await repo.BeginSessionFor(aliceId, throwIfNotExists: true))
        {
            var state = session.GetCurrentState();
            AnsiConsole.MarkupLine($"  Alice updated balance: [green]{state.Balance:C}[/] ({state.TransactionCount} transactions)");
        }
        AnsiConsole.WriteLine();

        // ── Step 4: Contains and Delete ────────────────────────
        AnsiConsole.MarkupLine("[yellow]Step 4:[/] Stream operations against PostgreSQL...");

        bool aliceExists = await repo.Contains(aliceId);
        bool bobExists = await repo.Contains(bobId);
        bool ghostExists = await repo.Contains("nonexistent-stream");

        var opsTable = new Table().Border(TableBorder.Rounded);
        opsTable.AddColumn("[bold]Operation[/]");
        opsTable.AddColumn("[bold]Result[/]");
        opsTable.AddRow($"Contains(\"{aliceId[..20]}...\")", aliceExists ? "[green]true[/]" : "[red]false[/]");
        opsTable.AddRow($"Contains(\"{bobId[..20]}...\")", bobExists ? "[green]true[/]" : "[red]false[/]");
        opsTable.AddRow("Contains(\"nonexistent-stream\")", ghostExists ? "[green]true[/]" : "[red]false[/]");
        AnsiConsole.Write(opsTable);

        AnsiConsole.MarkupLine("[yellow]Deleting Bob's stream from PostgreSQL...[/]");
        await repo.Delete(bobId);
        AnsiConsole.MarkupLine($"  Contains(\"{bobId[..20]}...\"): [red]{await repo.Contains(bobId)}[/]");
        AnsiConsole.WriteLine();

        // ── Step 5: Point-in-time query ────────────────────────
        AnsiConsole.Write(new Panel(
            "[dim]Point-in-time queries filter events by created_at timestamp.\n" +
            "Only events created on or before the appliesAt date are loaded.[/]")
            .Header("[yellow]Step 5: Point-in-Time Query[/]")
            .Border(BoxBorder.Rounded));

        // Query Alice's state as of before the bonus (events are very recent, so use a small offset)
        var timeBeforeBonus = DateTime.UtcNow.AddSeconds(-2);
        using (var historicalSession = await repo.BeginSessionFor(aliceId, throwIfNotExists: false, appliesAt: timeBeforeBonus))
        {
            var historicalState = historicalSession.GetCurrentState();
            using var currentSession = await repo.BeginSessionFor(aliceId, throwIfNotExists: true);
            var currentState = currentSession.GetCurrentState();

            var timeTable = new Table().Border(TableBorder.Rounded);
            timeTable.AddColumn("[bold]Query[/]");
            timeTable.AddColumn("[bold]Balance[/]");
            timeTable.AddColumn("[bold]Transactions[/]");
            timeTable.AddRow("Current", $"[green]{currentState.Balance:C}[/]", currentState.TransactionCount.ToString());
            timeTable.AddRow($"As of ~2 seconds ago", $"[yellow]{historicalState.Balance:C}[/]", historicalState.TransactionCount.ToString());
            AnsiConsole.Write(timeTable);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]All PostgreSQL persistence operations completed successfully.[/]");
    }
}
