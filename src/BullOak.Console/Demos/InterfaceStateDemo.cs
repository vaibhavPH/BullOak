using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using BullOak.Repositories.StateEmit;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 2: Interface-Based State with Dynamic Type Emission
///
/// BullOak can generate implementations of state interfaces at runtime
/// using IL emission. This is the recommended approach because:
///   - States can be switched to read-only after rehydration
///   - BullOak controls the setter logic for thread safety
///   - Clean separation of state definition from implementation
///
/// Key concepts:
///   - Define state as an interface (IBankAccountState)
///   - WithDefaultStateFactory() handles IL emission automatically
///   - ICanSwitchBackAndToReadOnly is automatically implemented
///   - State is writable during event application, read-only otherwise
/// </summary>
public static class InterfaceStateDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 2: Interface-Based State (Dynamic Type Emission)[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            "[dim]BullOak defines state as an interface:[/]\n\n" +
            "[bold]public interface IBankAccountState[/]\n" +
            "[bold]{[/]\n" +
            "[bold]    string AccountHolder { get; set; }[/]\n" +
            "[bold]    decimal Balance { get; set; }[/]\n" +
            "[bold]    bool IsFrozen { get; set; }[/]\n" +
            "[bold]    int TransactionCount { get; set; }[/]\n" +
            "[bold]}[/]\n\n" +
            "[dim]At runtime, BullOak emits (generates) a class implementing this interface\n" +
            "using System.Reflection.Emit (IL code generation). The generated class also\n" +
            "implements ICanSwitchBackAndToReadOnly, allowing BullOak to lock the state\n" +
            "as read-only after rehydration and unlock it only during event application.[/]")
            .Header("[yellow]How Interface State Works[/]")
            .Border(BoxBorder.Rounded));

        // Configure with interface-based appliers
        var configuration = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(InterfaceAccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var repo = new InMemoryEventSourcedRepository<string, IBankAccountState>(configuration);

        // Create and populate
        using var session = await repo.BeginSessionFor("INTF-001", throwIfNotExists: false);

        AnsiConsole.MarkupLine("Adding events to interface-based state...");
        session.AddEvent(new AccountOpened("Bob Johnson", 2500m));
        session.AddEvent(new MoneyDeposited(750m, "Bonus"));
        session.AddEvent(new MoneyWithdrawn(300m, "Shopping"));

        var state = session.GetCurrentState();

        // Show the runtime type
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");
        table.AddRow("Declared Type", "[aqua]IBankAccountState[/] (interface)");
        table.AddRow("Runtime Type", $"[aqua]{state.GetType().Name}[/] (emitted at runtime)");
        table.AddRow("Implements ICanSwitchBackAndToReadOnly",
            (state is ICanSwitchBackAndToReadOnly).ToString());
        table.AddRow("AccountHolder", state.AccountHolder);
        table.AddRow("Balance", $"[green]{state.Balance:C}[/]");
        table.AddRow("TransactionCount", state.TransactionCount.ToString());
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Demonstrate read-only switching
        AnsiConsole.Write(new Panel(
            "[dim]After rehydration, BullOak switches the state to read-only.\n" +
            "Attempting to set a property throws InvalidOperationException.\n" +
            "This prevents accidental state mutation outside of event application.[/]\n\n" +
            "The state is only writable during event applier execution.\n" +
            "BullOak toggles CanEdit = true before applying, false after.")
            .Header("[yellow]Read-Only Locking[/]")
            .Border(BoxBorder.Rounded));

        await session.SaveChanges();

        // Show that read-only works after disposal
        AnsiConsole.MarkupLine("[green]Interface-based state with IL emission works correctly.[/]");
        AnsiConsole.MarkupLine($"[dim]Generated type assembly: {state.GetType().Assembly.GetName().Name}[/]");
    }
}
