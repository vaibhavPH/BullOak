using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Appliers;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 4: Event Upconversion (Schema Evolution)
///
/// Over time, event schemas change. Old events in the store may have
/// different shapes than what the current code expects. Upconverters
/// transform old events into the current version during rehydration.
///
/// Chain: MoneyDepositedV1 → MoneyDepositedV2 → MoneyDepositedV3
///
/// Key concepts:
///   - IUpconvertEvent&lt;TSource, TDestination&gt; for 1:1 transforms
///   - IUpconvertEvent&lt;TSource&gt; for 1:many transforms
///   - WithUpconvertersFrom(assembly) for auto-discovery
///   - Recursive upconversion (V1 → V2 → V3 in one step)
/// </summary>
public static class UpconversionDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 4: Event Upconversion (Schema Evolution)[/]").LeftJustified());
        AnsiConsole.WriteLine();

        // Show the upconversion chain
        var tree = new Tree("[bold]Event Schema Evolution[/]");
        var v1Node = tree.AddNode("[red]MoneyDepositedV1[/] (Amount only)");
        var v2Node = v1Node.AddNode("[yellow]MoneyDepositedV2[/] (Amount + Description)");
        v2Node.AddNode("[green]MoneyDepositedV3[/] (Amount + Description + Timestamp + Currency)");
        AnsiConsole.Write(tree);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            "[bold]class MoneyDepositedV1ToV2 : IUpconvertEvent<MoneyDepositedV1, MoneyDepositedV2>[/]\n" +
            "[bold]{[/]\n" +
            "[bold]    public MoneyDepositedV2 Upconvert(MoneyDepositedV1 source)[/]\n" +
            "[bold]        => new(source.Amount, \"Legacy deposit (no description)\");[/]\n" +
            "[bold]}[/]\n\n" +
            "[bold]class MoneyDepositedV2ToV3 : IUpconvertEvent<MoneyDepositedV2, MoneyDepositedV3>[/]\n" +
            "[bold]{[/]\n" +
            "[bold]    public MoneyDepositedV3 Upconvert(MoneyDepositedV2 source)[/]\n" +
            "[bold]        => new(source.Amount, source.Description, DateTime.UtcNow, \"GBP\");[/]\n" +
            "[bold]}[/]\n\n" +
            "[dim]BullOak chains upconverters automatically. A stored V1 event\n" +
            "is transformed: V1 → V2 → V3 before reaching the applier.\n" +
            "Only the V3 applier needs to exist in your codebase.[/]")
            .Header("[yellow]Upconverter Classes[/]")
            .Border(BoxBorder.Rounded));

        // Configure with upconverters
        var configuration = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            // Only the V3 applier — older versions are upconverted before reaching it
            .WithEventApplier<BankAccountState, AccountOpened>((state, e) =>
            {
                state.AccountHolder = e.AccountHolder;
                state.Balance = e.InitialDeposit;
                state.TransactionCount = 1;
                return state;
            })
            .WithEventApplier<BankAccountState, MoneyDepositedV3>((state, e) =>
            {
                state.Balance += e.Amount;
                state.TransactionCount++;
                return state;
            })
            .AndNoMoreAppliers()
            // Auto-discover upconverters from this assembly
            .WithUpconvertersFrom(typeof(MoneyDepositedV1ToV2).Assembly)
            .AndNoMoreUpconverters()
            .Build();

        var repo = new InMemoryEventSourcedRepository<string, BankAccountState>(configuration);

        // ── Simulate loading V1 events (as if from an old database) ──
        AnsiConsole.MarkupLine("[yellow]Simulating legacy events being stored as V1...[/]");

        // First, store some events including a V1 event
        // The InMemory repo stores StoredEvents directly, so we work through sessions
        using (var session = await repo.BeginSessionFor("UPCON-001", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Legacy User", 500m));
            // V1 event — the upconverter chain will transform this to V3 during rehydration
            session.AddEvent(new MoneyDepositedV1(100m));
            await session.SaveChanges();
        }

        AnsiConsole.MarkupLine("[dim]Stored: AccountOpened + MoneyDepositedV1[/]");
        AnsiConsole.WriteLine();

        // ── Rehydrate — V1 is upconverted to V3 automatically ──
        AnsiConsole.MarkupLine("[yellow]Rehydrating... BullOak applies upconversion chain:[/]");
        AnsiConsole.MarkupLine("  [red]MoneyDepositedV1[/] → [yellow]MoneyDepositedV2[/] → [green]MoneyDepositedV3[/]");
        AnsiConsole.WriteLine();

        using var reloadSession = await repo.BeginSessionFor("UPCON-001", throwIfNotExists: true);
        var state = reloadSession.GetCurrentState();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");
        table.AddRow("AccountHolder", state.AccountHolder);
        table.AddRow("Balance", $"[green]{state.Balance:C}[/]  (500 initial + 100 from V1 event)");
        table.AddRow("TransactionCount", state.TransactionCount.ToString());
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("[green]V1 event was seamlessly upconverted and applied via V3 applier.[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            "[dim]Upconversion happens during rehydration (read time), not at write time.\n" +
            "The original V1 event remains unchanged in the store.\n" +
            "This means you never need to migrate old events in the database — they\n" +
            "are transformed on-the-fly every time they are loaded.\n\n" +
            "Use cases:\n" +
            "  - Renaming properties\n" +
            "  - Adding new required fields with defaults\n" +
            "  - Splitting one event into multiple events (use IUpconvertEvent<TSource>)\n" +
            "  - Merging event fields\n" +
            "  - Changing data types[/]")
            .Header("[yellow]When to Use Upconversion[/]")
            .Border(BoxBorder.Rounded));
    }
}
