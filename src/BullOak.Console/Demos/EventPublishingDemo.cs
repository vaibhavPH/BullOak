using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 5: Event Publishing
///
/// BullOak can publish events to an external bus/broker during SaveChanges.
/// Three publisher options:
///   1. WithNoEventPublisher()          — No publishing (events only stored)
///   2. WithEventPublisher(Action)      — Synchronous delegate
///   3. WithEventPublisher(Func&lt;Task&gt;)  — Async delegate
///   4. WithEventPublisher(IPublishEvents) — Full interface implementation
///
/// Combined with DeliveryTargetGuarntee:
///   - AtLeastOnce: Publish BEFORE save (may duplicate on failure)
///   - AtMostOnce:  Publish AFTER save (may miss on failure)
/// </summary>
public static class EventPublishingDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 5: Event Publishing[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var publishedEvents = new List<string>();

        // ── Synchronous publisher ──────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold].WithEventPublisher(item => { /* handle event */ })[/]\n\n" +
            "[dim]The ItemWithType struct contains:\n" +
            "  - .instance (object) — the event object\n" +
            "  - .type (Type) — the CLR type of the event\n\n" +
            "Sync publishers are called inline during SaveChanges.\n" +
            "Use for: logging, metrics, in-process notification.[/]")
            .Header("[yellow]Synchronous Event Publisher[/]")
            .Border(BoxBorder.Rounded));

        var syncConfig = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithEventPublisher(item =>
            {
                publishedEvents.Add($"[sync] {item.type.Name}");
                AnsiConsole.MarkupLine($"  [dim][[published/sync]][/] [aqua]{item.type.Name}[/]: {item.instance}");
            })
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var syncRepo = new InMemoryEventSourcedRepository<string, BankAccountState>(syncConfig);
        using (var session = await syncRepo.BeginSessionFor("PUB-SYNC", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Sync Test", 100m));
            session.AddEvent(new MoneyDeposited(50m, "Sync deposit"));
            AnsiConsole.MarkupLine("[yellow]Saving with sync publisher...[/]");
            await session.SaveChanges();
        }
        AnsiConsole.WriteLine();

        // ── Async publisher ────────────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold].WithEventPublisher(async (item, cancellationToken) => { ... })[/]\n\n" +
            "[dim]Async publishers support cancellation tokens.\n" +
            "Use for: HTTP calls to message brokers, database writes,\n" +
            "or any I/O-bound publishing operation.[/]")
            .Header("[yellow]Asynchronous Event Publisher[/]")
            .Border(BoxBorder.Rounded));

        var asyncConfig = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithEventPublisher(async (item, ct) =>
            {
                // Simulate async publishing (e.g., sending to RabbitMQ, Kafka, etc.)
                await Task.Delay(10, ct);
                publishedEvents.Add($"[async] {item.type.Name}");
                AnsiConsole.MarkupLine($"  [dim][[published/async]][/] [aqua]{item.type.Name}[/]: {item.instance}");
            })
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var asyncRepo = new InMemoryEventSourcedRepository<string, BankAccountState>(asyncConfig);
        using (var session = await asyncRepo.BeginSessionFor("PUB-ASYNC", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Async Test", 200m));
            session.AddEvent(new MoneyDeposited(75m, "Async deposit"));
            AnsiConsole.MarkupLine("[yellow]Saving with async publisher...[/]");
            await session.SaveChanges();
        }
        AnsiConsole.WriteLine();

        // ── No publisher ───────────────────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold].WithNoEventPublisher()[/]\n\n" +
            "[dim]Events are stored but not published anywhere.\n" +
            "Use for: testing, or when consumers read directly from the event store.[/]")
            .Header("[yellow]No Publisher (Null Publisher)[/]")
            .Border(BoxBorder.Rounded));

        // Summary
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Published Events[/]");
        foreach (var evt in publishedEvents)
            table.AddRow(evt);
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine($"[green]Total events published: {publishedEvents.Count}[/]");
    }
}
