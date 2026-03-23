using BullOak.Console.Demos;
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
//  Built with Spectre.Console for a rich terminal experience.
// ═══════════════════════════════════════════════════════════════

var demos = new (string Name, string Description, Func<Task> Action)[]
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

// ── Non-interactive mode: --run-all or --demo N ────────────────
if (args.Contains("--run-all"))
{
    await RunAllDemos();
    return;
}

var demoIndex = Array.IndexOf(args, "--demo");
if (demoIndex >= 0 && demoIndex + 1 < args.Length && int.TryParse(args[demoIndex + 1], out int demoNum))
{
    if (demoNum >= 1 && demoNum <= demos.Length)
    {
        var (name, _, action) = demos[demoNum - 1];
        AnsiConsole.Write(new Rule($"[bold green]Demo {demoNum}: {name}[/]").LeftJustified());
        AnsiConsole.WriteLine();
        await action();
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]Invalid demo number. Choose 1-{demos.Length}.[/]");
    }
    return;
}

// ── Interactive mode ───────────────────────────────────────────
ShowHeader();

while (true)
{
    AnsiConsole.Write(new Rule("[bold yellow]Main Menu[/]").LeftJustified());
    AnsiConsole.WriteLine();

    // Build menu choices: numbered demos + Run All + Exit
    var choices = new List<string>();
    for (int i = 0; i < demos.Length; i++)
        choices.Add($"{i + 1,2}. {demos[i].Name}");
    choices.Add("--  Run All Demos");
    choices.Add("--  Exit");

    var selection = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[bold]Choose a demo to run:[/]")
            .PageSize(18)
            .HighlightStyle(new Style(Color.Blue, decoration: Decoration.Bold))
            .AddChoices(choices)
            .UseConverter(choice =>
            {
                if (choice.StartsWith("--")) return choice;
                // Extract index to get description
                var dotIdx = choice.IndexOf('.');
                if (dotIdx > 0 && int.TryParse(choice[..dotIdx].Trim(), out int idx) && idx >= 1 && idx <= demos.Length)
                    return $"{choice}  [dim]— {demos[idx - 1].Description}[/]";
                return choice;
            }));

    if (selection == "--  Exit")
    {
        AnsiConsole.MarkupLine("[dim]Goodbye![/]");
        break;
    }

    AnsiConsole.Clear();

    if (selection == "--  Run All Demos")
    {
        await RunAllDemos();
    }
    else
    {
        // Parse selected demo number
        var dotPos = selection.IndexOf('.');
        if (dotPos > 0 && int.TryParse(selection[..dotPos].Trim(), out int selectedIdx) && selectedIdx >= 1 && selectedIdx <= demos.Length)
        {
            try
            {
                await demos[selectedIdx - 1].Action();
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
}

// ── Helper methods ─────────────────────────────────────────────

async Task RunAllDemos()
{
    for (int i = 0; i < demos.Length; i++)
    {
        var (name, _, action) = demos[i];
        AnsiConsole.Write(new Rule($"[bold green]Demo {i + 1}: {name}[/]").LeftJustified());
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

    AnsiConsole.MarkupLine("[bold green]All demos completed successfully![/]");
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
