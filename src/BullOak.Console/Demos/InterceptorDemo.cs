using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 7: Event Interceptors (Middleware)
///
/// Interceptors hook into four lifecycle points:
///   - BeforePublish(event, eventType, state, stateType)
///   - AfterPublish(event, eventType, state, stateType)
///   - BeforeSave(event, eventType, state, stateType)
///   - AfterSave(event, eventType, state, stateType)
///
/// Multiple interceptors can be chained. They execute in registration order.
/// Use cases: logging, metrics, audit trails, cross-cutting concerns.
/// </summary>
public static class InterceptorDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 7: Event Interceptors (Middleware)[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            "[bold]public interface IInterceptEvents[/]\n" +
            "[bold]{[/]\n" +
            "[bold]    void BeforePublish(object @event, Type typeOfEvent, object state, Type typeOfState);[/]\n" +
            "[bold]    void AfterPublish(object @event, Type typeOfEvent, object state, Type typeOfState);[/]\n" +
            "[bold]    void BeforeSave(object @event, Type typeOfEvent, object state, Type typeOfState);[/]\n" +
            "[bold]    void AfterSave(object @event, Type typeOfEvent, object state, Type typeOfState);[/]\n" +
            "[bold]}[/]\n\n" +
            "[dim]Register with: .WithInterceptor(new MyInterceptor())\n" +
            "Multiple interceptors can be chained on any IConfigureBullOak step.[/]")
            .Header("[yellow]IInterceptEvents Interface[/]")
            .Border(BoxBorder.Rounded));

        var loggingInterceptor = new LoggingInterceptor();
        var metricsInterceptor = new MetricsInterceptor();

        // Chain two interceptors
        var config = Configuration.Begin()
            .WithDefaultCollection()
            .WithInterceptor(loggingInterceptor)   // first interceptor
            .WithInterceptor(metricsInterceptor)    // second interceptor (chained)
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var repo = new InMemoryEventSourcedRepository<string, BankAccountState>(config);

        AnsiConsole.MarkupLine("[yellow]Saving events with two interceptors chained...[/]");
        AnsiConsole.MarkupLine("[dim](LoggingInterceptor prints to console, MetricsInterceptor counts silently)[/]");
        AnsiConsole.WriteLine();

        using (var session = await repo.BeginSessionFor("INTCPT-001", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Intercepted User", 1000m));
            session.AddEvent(new MoneyDeposited(250m, "Intercepted deposit"));
            session.AddEvent(new MoneyWithdrawn(100m, "Intercepted withdrawal"));

            await session.SaveChanges();
        }

        AnsiConsole.WriteLine();

        // Show metrics from the MetricsInterceptor
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Metric[/]");
        table.AddColumn("[bold]Count[/]");
        table.AddRow("Events published (BeforePublish count)", metricsInterceptor.PublishCount.ToString());
        table.AddRow("Events saved (BeforeSave count)", metricsInterceptor.SaveCount.ToString());
        AnsiConsole.Write(table);

        AnsiConsole.Write(new Panel(
            "[dim]Interceptors are great for cross-cutting concerns:\n" +
            "  - Audit logging (who changed what, when)\n" +
            "  - Performance metrics (event processing times)\n" +
            "  - Notification triggers (email on specific events)\n" +
            "  - Debug tracing (event flow visualization)\n\n" +
            "They run for EVERY event during SaveChanges,\n" +
            "receiving both the event and the current state.\n" +
            "Chain multiple interceptors — they execute in registration order.[/]")
            .Header("[yellow]Use Cases[/]")
            .Border(BoxBorder.Rounded));
    }
}
