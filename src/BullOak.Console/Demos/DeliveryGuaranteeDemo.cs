using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 6: Delivery Guarantees
///
/// When publishing events, you choose between two guarantees:
///
///   AtLeastOnce (default):
///     Events are published BEFORE being saved to the store.
///     If save fails after publish, events may be published again on retry.
///     Consumers must be idempotent (handle duplicates).
///
///   AtMostOnce:
///     Events are published AFTER being saved to the store.
///     If the process crashes after save but before publish, events are lost.
///     Simpler consumers, but possible data loss.
///
/// This is one of the most important architectural decisions in event sourcing.
/// </summary>
public static class DeliveryGuaranteeDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 6: Delivery Guarantees[/]").LeftJustified());
        AnsiConsole.WriteLine();

        // Visual explanation
        AnsiConsole.Write(new Panel(
            "[bold yellow]AtLeastOnce (default)[/]\n" +
            "  1. Events are [yellow]published[/] to the bus\n" +
            "  2. Events are [green]saved[/] to the store\n" +
            "  [dim]If step 2 fails → events were already published → duplicates possible\n" +
            "  Consumers MUST be idempotent (handle same event twice)[/]\n\n" +
            "[bold yellow]AtMostOnce[/]\n" +
            "  1. Events are [green]saved[/] to the store\n" +
            "  2. Events are [yellow]published[/] to the bus\n" +
            "  [dim]If step 2 fails → events are saved but never published → data loss possible\n" +
            "  Simpler consumers, but events may be missed[/]")
            .Header("[yellow]The Tradeoff[/]")
            .Border(BoxBorder.Rounded));

        var publishLog = new List<(string Phase, string EventName, string Guarantee)>();

        var config = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithEventPublisher(item =>
            {
                // This runs either before or after save depending on the guarantee
                publishLog.Add(("Published", item.type.Name, ""));
            })
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        // ── AtLeastOnce ────────────────────────────────────────
        AnsiConsole.MarkupLine("[bold yellow]Testing AtLeastOnce:[/]");
        var repo1 = new InMemoryEventSourcedRepository<string, BankAccountState>(config);
        using (var session = await repo1.BeginSessionFor("DG-001", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("AtLeastOnce User", 1000m));
            session.AddEvent(new MoneyDeposited(100m, "Deposit"));

            publishLog.Clear();
            // AtLeastOnce is the default
            int saved = await session.SaveChanges(DeliveryTargetGuarntee.AtLeastOnce);
            AnsiConsole.MarkupLine($"  Events saved: [green]{saved}[/]");
            AnsiConsole.MarkupLine($"  Events published: [green]{publishLog.Count}[/]");
            AnsiConsole.MarkupLine("  [dim]Order: Publish → Save (events published first)[/]");
        }
        AnsiConsole.WriteLine();

        // ── AtMostOnce ─────────────────────────────────────────
        AnsiConsole.MarkupLine("[bold yellow]Testing AtMostOnce:[/]");
        var repo2 = new InMemoryEventSourcedRepository<string, BankAccountState>(config);
        using (var session = await repo2.BeginSessionFor("DG-002", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("AtMostOnce User", 2000m));
            session.AddEvent(new MoneyWithdrawn(300m, "Withdrawal"));

            publishLog.Clear();
            int saved = await session.SaveChanges(DeliveryTargetGuarntee.AtMostOnce);
            AnsiConsole.MarkupLine($"  Events saved: [green]{saved}[/]");
            AnsiConsole.MarkupLine($"  Events published: [green]{publishLog.Count}[/]");
            AnsiConsole.MarkupLine("  [dim]Order: Save → Publish (events saved first)[/]");
        }
        AnsiConsole.WriteLine();

        // Comparison table
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Aspect[/]");
        table.AddColumn("[bold]AtLeastOnce[/]");
        table.AddColumn("[bold]AtMostOnce[/]");
        table.AddRow("Publish order", "Before save", "After save");
        table.AddRow("On save failure", "Events already published (duplicates)", "Events not published (lost)");
        table.AddRow("Consumer requirement", "Must be idempotent", "No special requirement");
        table.AddRow("Data consistency", "Higher (eventual)", "Lower (possible gaps)");
        table.AddRow("Complexity", "Higher", "Lower");
        table.AddRow("Default", "[green]Yes[/]", "No");
        AnsiConsole.Write(table);

        AnsiConsole.Write(new Panel(
            "[dim]This choice is the single most important design decision in your event-sourced\n" +
            "ecosystem. AtLeastOnce is the default because data loss is generally worse than\n" +
            "duplicates — but it requires all consumers to be idempotent.[/]")
            .Header("[yellow]Recommendation[/]")
            .Border(BoxBorder.Rounded));
    }
}
