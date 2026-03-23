using BullOak.Console.Demos;
using BullOak.Console.Infrastructure;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

// ═══════════════════════════════════════════════════════════════
//  BullOak Showcase Console Application
//
//  An interactive learning tool that demonstrates every feature
//  of the BullOak event sourcing library. Each demo is self-
//  contained and can be run independently.
//
//  Usage:
//    dotnet run                  → Interactive menu (arrow keys + Enter)
//    dotnet run -- --run-all     → Run all demos sequentially (non-interactive)
//    dotnet run -- --demo 3      → Run a specific demo by number
//
//  Configuration:
//    Edit appsettings.json to choose between TestContainers (Docker)
//    or external database connections for PostgreSQL and EventStoreDB.
//
//  Built with Spectre.Console for a rich terminal experience.
// ═══════════════════════════════════════════════════════════════

// ── Load configuration ─────────────────────────────────────────
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var infraSettings = new InfrastructureSettings();
configuration.GetSection("Infrastructure").Bind(infraSettings);

// ── Create infrastructure manager (disposed on exit) ───────────
await using var infra = new InfrastructureManager(infraSettings);

// ── Define demos ───────────────────────────────────────────────
// In-memory demos (no external infrastructure needed)
var inMemoryDemos = new (string Name, string Description, Func<Task> Action)[]
{
    ("Basic Event Sourcing",
        "Core workflow: configure, create repo, begin session, add events, save",
        BasicEventSourcingDemo.RunAsync),

    ("Interface-Based State",
        "Dynamic type emission for interface states with read-only locking",
        InterfaceStateDemo.RunAsync),

    ("Applier Configuration",
        "Four approaches: lambda, class-based, assembly scan, instance list",
        ApplierConfigurationDemo.RunAsync),

    ("Event Upconversion",
        "Schema evolution: V1 → V2 → V3 automatic event transformation",
        UpconversionDemo.RunAsync),

    ("Event Publishing",
        "Sync/async publishers, null publisher, ItemWithType structure",
        EventPublishingDemo.RunAsync),

    ("Delivery Guarantees",
        "AtLeastOnce vs AtMostOnce: the most important architectural decision",
        DeliveryGuaranteeDemo.RunAsync),

    ("Interceptors (Middleware)",
        "BeforePublish/AfterPublish/BeforeSave/AfterSave lifecycle hooks",
        InterceptorDemo.RunAsync),

    ("State Validation",
        "Business rules, ValidationResults, BusinessException on failure",
        ValidationDemo.RunAsync),

    ("Optimistic Concurrency",
        "Concurrent sessions, ConcurrencyException, retry pattern",
        ConcurrencyDemo.RunAsync),

    ("Point-in-Time Queries",
        "Temporal queries with appliesAt: state at any historical moment",
        PointInTimeQueryDemo.RunAsync),

    ("Stream Operations",
        "Contains, Delete, throwIfNotExists, AddEvent overloads",
        StreamOperationsDemo.RunAsync),

    ("Thread Safety",
        "AlwaysUseThreadSafe, NeverUseThreadSafe, per-type selector",
        ThreadSafetyDemo.RunAsync),

    ("Cinema Reservation",
        "Second domain: seat booking with Dictionary<string,string> state",
        CinemaReservationDemo.RunAsync),

    ("Full Interactive Workflow",
        "Everything together: multi-account bank with all features combined",
        FullWorkflowDemo.RunAsync),
};

// Persistence demos (require Docker or external databases)
var persistenceDemos = new (string Name, string Description, Func<Task> Action)[]
{
    ("PostgreSQL Persistence",
        $"Real PostgreSQL database ({infra.GetPostgreSqlMode()})",
        () => PostgreSqlPersistenceDemo.RunAsync(infra)),

    ("EventStoreDB Persistence",
        $"Real EventStoreDB instance ({infra.GetEventStoreMode()})",
        () => EventStorePersistenceDemo.RunAsync(infra)),
};

// Combined list for numbering
var allDemos = inMemoryDemos.Concat(persistenceDemos).ToArray();

// ── Non-interactive mode: --run-all or --demo N ────────────────
if (args.Contains("--run-all"))
{
    await RunAllDemos();
    return;
}

var demoIndex = Array.IndexOf(args, "--demo");
if (demoIndex >= 0 && demoIndex + 1 < args.Length && int.TryParse(args[demoIndex + 1], out int demoNum))
{
    if (demoNum >= 1 && demoNum <= allDemos.Length)
    {
        var (name, _, action) = allDemos[demoNum - 1];
        AnsiConsole.Write(new Rule($"[bold green]Demo {demoNum}: {name}[/]").LeftJustified());
        AnsiConsole.WriteLine();
        await action();
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]Invalid demo number. Choose 1-{allDemos.Length}.[/]");
    }
    return;
}

// ── Interactive mode ───────────────────────────────────────────
ShowHeader();
ShowInfraStatus();

while (true)
{
    AnsiConsole.Write(new Rule("[bold yellow]Main Menu[/]").LeftJustified());
    AnsiConsole.WriteLine();

    // Build menu choices with section headers
    var choices = new List<string>();

    // In-memory demos
    for (int i = 0; i < inMemoryDemos.Length; i++)
        choices.Add($"{i + 1,2}. {inMemoryDemos[i].Name}");

    // Separator + persistence demos
    choices.Add("──  [[Persistence Demos - Docker/External DB]]");
    for (int i = 0; i < persistenceDemos.Length; i++)
        choices.Add($"{inMemoryDemos.Length + i + 1,2}. {persistenceDemos[i].Name}");

    // Actions
    choices.Add("──  [[Actions]]");
    choices.Add(">>  Run All In-Memory Demos");
    choices.Add(">>  Run All Demos (including persistence)");
    choices.Add(">>  Exit");

    var selection = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[bold]Choose a demo to run:[/]")
            .PageSize(22)
            .HighlightStyle(new Style(Color.Blue, decoration: Decoration.Bold))
            .AddChoices(choices)
            .UseConverter(choice =>
            {
                if (choice.StartsWith("──")) return $"[dim]{choice}[/]";
                if (choice.StartsWith(">>")) return choice;
                var dotIdx = choice.IndexOf('.');
                if (dotIdx > 0 && int.TryParse(choice[..dotIdx].Trim(), out int idx) && idx >= 1 && idx <= allDemos.Length)
                    return $"{choice}  [dim]— {allDemos[idx - 1].Description}[/]";
                return choice;
            }));

    // Handle section headers (not selectable, just re-prompt)
    if (selection.StartsWith("──"))
        continue;

    if (selection == ">>  Exit")
    {
        AnsiConsole.MarkupLine("[dim]Shutting down...[/]");
        break;
    }

    AnsiConsole.Clear();

    if (selection == ">>  Run All In-Memory Demos")
    {
        await RunDemos(inMemoryDemos, "In-Memory");
    }
    else if (selection == ">>  Run All Demos (including persistence)")
    {
        await RunAllDemos();
    }
    else
    {
        var dotPos = selection.IndexOf('.');
        if (dotPos > 0 && int.TryParse(selection[..dotPos].Trim(), out int selectedIdx)
            && selectedIdx >= 1 && selectedIdx <= allDemos.Length)
        {
            try
            {
                await allDemos[selectedIdx - 1].Action();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            }
        }
    }

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[dim]Press any key to return to the menu...[/]");
    System.Console.ReadKey(intercept: true);
    AnsiConsole.Clear();
    ShowHeader();
    ShowInfraStatus();
}

// ── Helper methods ─────────────────────────────────────────────

async Task RunAllDemos()
{
    await RunDemos(inMemoryDemos, "In-Memory");
    AnsiConsole.WriteLine();
    await RunDemos(persistenceDemos, "Persistence", startIndex: inMemoryDemos.Length);
    AnsiConsole.MarkupLine("[bold green]All demos completed![/]");
}

async Task RunDemos(
    (string Name, string Description, Func<Task> Action)[] demoList,
    string sectionLabel,
    int startIndex = 0)
{
    AnsiConsole.Write(new Rule($"[bold green]{sectionLabel} Demos[/]").LeftJustified());
    AnsiConsole.WriteLine();

    for (int i = 0; i < demoList.Length; i++)
    {
        var (name, _, action) = demoList[i];
        AnsiConsole.Write(new Rule($"[bold green]Demo {startIndex + i + 1}: {name}[/]").LeftJustified());
        AnsiConsole.WriteLine();

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("dim")));
        AnsiConsole.WriteLine();
    }
}

void ShowHeader()
{
    AnsiConsole.Write(new FigletText("BullOak")
        .Color(Color.Blue)
        .Centered());
    AnsiConsole.Write(new Markup("[bold]Event Sourcing Library — Interactive Showcase[/]").Centered());
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Markup("[dim]Navigate with arrow keys, press Enter to select. Or use: --run-all | --demo N[/]").Centered());
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine();
}

void ShowInfraStatus()
{
    var table = new Table().Border(TableBorder.Rounded).Expand();
    table.AddColumn("[bold]Infrastructure[/]");
    table.AddColumn("[bold]Mode[/]");
    table.AddColumn("[bold]Status[/]");

    table.AddRow(
        "PostgreSQL",
        infra.GetPostgreSqlMode(),
        infra.IsPostgreSqlInitialized ? "[green]Running[/]" : "[dim]Not started (starts on first use)[/]");

    table.AddRow(
        "EventStoreDB",
        infra.GetEventStoreMode(),
        infra.IsEventStoreInitialized ? "[green]Running[/]" : "[dim]Not started (starts on first use)[/]");

    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();
}
