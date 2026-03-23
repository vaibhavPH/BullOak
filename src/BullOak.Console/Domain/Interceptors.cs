using BullOak.Repositories.Middleware;
using Spectre.Console;

namespace BullOak.Console.Domain;

// ─────────────────────────────────────────────────────────────
//  Event Interceptors (Middleware)
//
//  IInterceptEvents lets you hook into the event lifecycle:
//    - BeforePublish / AfterPublish
//    - BeforeSave / AfterSave
//
//  Use cases: logging, metrics, audit trails, side effects.
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Logs every event lifecycle step to the console with Spectre.Console markup.
/// In production you'd send these to a logging framework or metrics system.
/// </summary>
public class LoggingInterceptor : IInterceptEvents
{
    public void BeforePublish(object @event, Type typeOfEvent, object state, Type typeOfState)
    {
        AnsiConsole.MarkupLine($"  [dim][[interceptor]][/] [yellow]BeforePublish[/]  event=[aqua]{typeOfEvent.Name}[/]");
    }

    public void AfterPublish(object @event, Type typeOfEvent, object state, Type typeOfState)
    {
        AnsiConsole.MarkupLine($"  [dim][[interceptor]][/] [green]AfterPublish[/]   event=[aqua]{typeOfEvent.Name}[/]");
    }

    public void BeforeSave(object @event, Type typeOfEvent, object state, Type typeOfState)
    {
        AnsiConsole.MarkupLine($"  [dim][[interceptor]][/] [yellow]BeforeSave[/]     event=[aqua]{typeOfEvent.Name}[/]");
    }

    public void AfterSave(object @event, Type typeOfEvent, object state, Type typeOfState)
    {
        AnsiConsole.MarkupLine($"  [dim][[interceptor]][/] [green]AfterSave[/]      event=[aqua]{typeOfEvent.Name}[/]");
    }
}

/// <summary>
/// Counts how many events pass through each lifecycle hook.
/// Demonstrates collecting metrics from interceptors.
/// </summary>
public class MetricsInterceptor : IInterceptEvents
{
    public int PublishCount { get; private set; }
    public int SaveCount { get; private set; }

    public void BeforePublish(object @event, Type typeOfEvent, object state, Type typeOfState)
        => PublishCount++;

    public void AfterPublish(object @event, Type typeOfEvent, object state, Type typeOfState) { }

    public void BeforeSave(object @event, Type typeOfEvent, object state, Type typeOfState)
        => SaveCount++;

    public void AfterSave(object @event, Type typeOfEvent, object state, Type typeOfState) { }
}
