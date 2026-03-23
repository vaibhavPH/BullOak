using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 12: Thread Safety Configuration
///
/// BullOak offers three thread-safety modes:
///   - AlwaysUseThreadSafe(): All state types use thread-safe operations
///   - NeverUseThreadSafe(): No thread-safe operations (best performance)
///   - WithThreadSafetySelector(func): Per-type control
///
/// Thread-safe mode adds synchronization around event collection and
/// state mutations. This is needed when multiple threads access the
/// same session, but comes with performance overhead.
///
/// In message-driven architectures (actors, message buses), you
/// typically don't need thread safety because each message is
/// processed sequentially.
/// </summary>
public static class ThreadSafetyDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 12: Thread Safety Configuration[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            "[bold]// Option 1: Always thread-safe (safer, slower)[/]\n" +
            "[bold].AlwaysUseThreadSafe()[/]\n\n" +
            "[bold]// Option 2: Never thread-safe (faster, single-threaded only)[/]\n" +
            "[bold].NeverUseThreadSafe()[/]\n\n" +
            "[bold]// Option 3: Per-type selector[/]\n" +
            "[bold].WithThreadSafetySelector(stateType =>[/]\n" +
            "[bold]    stateType == typeof(BankAccountState))[/]\n\n" +
            "[dim]Thread-safe mode wraps event collection operations in\n" +
            "synchronization primitives. When using interface-based states,\n" +
            "it also controls the ICanSwitchBackAndToReadOnly behavior.[/]")
            .Header("[yellow]Thread Safety Options[/]")
            .Border(BoxBorder.Rounded));

        // ── NeverUseThreadSafe ─────────────────────────────────
        AnsiConsole.MarkupLine("[bold yellow]Mode 1: NeverUseThreadSafe()[/]");
        var unsafeConfig = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var unsafeRepo = new InMemoryEventSourcedRepository<string, BankAccountState>(unsafeConfig);
        using (var session = await unsafeRepo.BeginSessionFor("TS-UNSAFE", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Unsafe User", 1000m));
            session.AddEvent(new MoneyDeposited(500m, "Fast deposit"));
            await session.SaveChanges();
            AnsiConsole.MarkupLine($"  Balance: [green]{session.GetCurrentState().Balance:C}[/]");
            AnsiConsole.MarkupLine("  [dim]Best performance, no synchronization overhead.[/]");
        }
        AnsiConsole.WriteLine();

        // ── AlwaysUseThreadSafe ────────────────────────────────
        AnsiConsole.MarkupLine("[bold yellow]Mode 2: AlwaysUseThreadSafe()[/]");
        var safeConfig = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .AlwaysUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var safeRepo = new InMemoryEventSourcedRepository<string, BankAccountState>(safeConfig);
        using (var session = await safeRepo.BeginSessionFor("TS-SAFE", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Safe User", 1000m));
            session.AddEvent(new MoneyDeposited(500m, "Safe deposit"));
            await session.SaveChanges();
            AnsiConsole.MarkupLine($"  Balance: [green]{session.GetCurrentState().Balance:C}[/]");
            AnsiConsole.MarkupLine("  [dim]Thread-safe operations, slight performance overhead.[/]");
        }
        AnsiConsole.WriteLine();

        // ── Per-type selector ──────────────────────────────────
        AnsiConsole.MarkupLine("[bold yellow]Mode 3: WithThreadSafetySelector (per-type)[/]");
        var selectiveConfig = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .WithThreadSafetySelector(stateType =>
                stateType == typeof(BankAccountState)) // Only BankAccountState is thread-safe
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var selectiveRepo = new InMemoryEventSourcedRepository<string, BankAccountState>(selectiveConfig);
        using (var session = await selectiveRepo.BeginSessionFor("TS-SELECT", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Selective User", 1000m));
            await session.SaveChanges();
            AnsiConsole.MarkupLine($"  Balance: [green]{session.GetCurrentState().Balance:C}[/]");
            AnsiConsole.MarkupLine("  [dim]BankAccountState = thread-safe; other types = not thread-safe.[/]");
        }
        AnsiConsole.WriteLine();

        // Comparison table
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Mode[/]");
        table.AddColumn("[bold]Performance[/]");
        table.AddColumn("[bold]Safety[/]");
        table.AddColumn("[bold]Use Case[/]");
        table.AddRow("NeverUseThreadSafe", "[green]Best[/]", "Single-threaded only", "Message-driven systems, web requests");
        table.AddRow("AlwaysUseThreadSafe", "[yellow]Good[/]", "Multi-threaded safe", "Shared sessions across threads");
        table.AddRow("Per-type selector", "[green]Optimized[/]", "Selective", "Mix of shared and single-threaded states");
        AnsiConsole.Write(table);

        AnsiConsole.Write(new Panel(
            "[dim]In most real-world applications, NeverUseThreadSafe() is the right choice.\n" +
            "Web requests and message handlers are naturally single-threaded per request.\n" +
            "Only use thread-safe mode when multiple threads genuinely share a session.[/]")
            .Header("[yellow]Recommendation[/]")
            .Border(BoxBorder.Rounded));
    }
}
