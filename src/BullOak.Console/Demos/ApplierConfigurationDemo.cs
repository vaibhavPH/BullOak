using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Appliers;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 3: Applier Configuration Approaches
///
/// BullOak supports multiple ways to register event appliers:
///   1. Lambda functions: WithEventApplier&lt;TState, TEvent&gt;((state, event) => ...)
///   2. Class-based: IApplyEvent&lt;TState, TEvent&gt; implementations
///   3. Assembly scanning: WithAnyAppliersFrom(assembly)
///   4. Instance-based: WithAnyAppliersFromInstances(list)
///
/// This demo shows all four approaches producing identical results.
/// </summary>
public static class ApplierConfigurationDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 3: Applier Configuration Approaches[/]").LeftJustified());
        AnsiConsole.WriteLine();

        // ── Approach 1: Lambda (inline) ────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold].WithEventApplier<BankAccountState, AccountOpened>([/]\n" +
            "[bold]    (state, e) => { state.AccountHolder = e.AccountHolder; ... return state; })[/]\n\n" +
            "[dim]Best for: Simple appliers, prototyping, quick demos.\n" +
            "Each lambda is a Func<TState, TEvent, TState>.[/]")
            .Header("[yellow]Approach 1: Lambda Functions[/]")
            .Border(BoxBorder.Rounded));

        var lambdaConfig = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithEventApplier<BankAccountState, AccountOpened>((state, e) =>
            {
                state.AccountHolder = e.AccountHolder;
                state.Balance = e.InitialDeposit;
                state.TransactionCount = 1;
                return state;
            })
            .WithEventApplier<BankAccountState, MoneyDeposited>((state, e) =>
            {
                state.Balance += e.Amount;
                state.TransactionCount++;
                return state;
            })
            .WithEventApplier<BankAccountState, MoneyWithdrawn>((state, e) =>
            {
                state.Balance -= e.Amount;
                state.TransactionCount++;
                return state;
            })
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var balance1 = await RunScenario(lambdaConfig, "Lambda");

        // ── Approach 2: Class-based with manual registration ───
        AnsiConsole.Write(new Panel(
            "[bold].WithEventApplier<BankAccountState, AccountOpened>(new AccountOpenedApplier())[/]\n\n" +
            "[dim]Best for: Complex applier logic, dependency injection.\n" +
            "Each class implements IApplyEvent<TState, TEvent>.\n" +
            "Register each applier individually.[/]")
            .Header("[yellow]Approach 2: Class Instances (Manual)[/]")
            .Border(BoxBorder.Rounded));

        var classConfig = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithEventApplier<BankAccountState, AccountOpened>(new AccountOpenedApplier())
            .WithEventApplier<BankAccountState, MoneyDeposited>(new MoneyDepositedApplier())
            .WithEventApplier<BankAccountState, MoneyWithdrawn>(new MoneyWithdrawnApplier())
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var balance2 = await RunScenario(classConfig, "Class-based");

        // ── Approach 3: Assembly scanning ──────────────────────
        AnsiConsole.Write(new Panel(
            "[bold].WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)[/]\n\n" +
            "[dim]Best for: Production applications with many appliers.\n" +
            "BullOak uses reflection to discover all types implementing\n" +
            "IApplyEvent<,> or IApplyEvents<> and registers them automatically.\n" +
            "Scans the entire assembly — zero manual registration needed.[/]")
            .Header("[yellow]Approach 3: Assembly Scanning[/]")
            .Border(BoxBorder.Rounded));

        var assemblyConfig = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var balance3 = await RunScenario(assemblyConfig, "Assembly Scan");

        // ── Approach 4: Instance list ──────────────────────────
        AnsiConsole.Write(new Panel(
            "[bold].WithAnyAppliersFromInstances(new object[] { ... })[/]\n\n" +
            "[dim]Best for: DI containers where appliers are resolved at runtime.\n" +
            "Pass a list of pre-created applier instances.\n" +
            "BullOak inspects their interfaces to determine (State, Event) pairs.[/]")
            .Header("[yellow]Approach 4: Instance List[/]")
            .Border(BoxBorder.Rounded));

        var instanceConfig = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFromInstances(new object[]
            {
                new AccountOpenedApplier(),
                new MoneyDepositedApplier(),
                new MoneyWithdrawnApplier()
            })
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var balance4 = await RunScenario(instanceConfig, "Instance List");

        // ── Comparison ─────────────────────────────────────────
        AnsiConsole.WriteLine();
        var resultTable = new Table().Border(TableBorder.Rounded);
        resultTable.AddColumn("[bold]Approach[/]");
        resultTable.AddColumn("[bold]Final Balance[/]");
        resultTable.AddColumn("[bold]Match?[/]");
        resultTable.AddRow("Lambda", $"{balance1:C}", balance1 == balance3 ? "[green]Yes[/]" : "[red]No[/]");
        resultTable.AddRow("Class-based", $"{balance2:C}", balance2 == balance3 ? "[green]Yes[/]" : "[red]No[/]");
        resultTable.AddRow("Assembly Scan", $"{balance3:C}", "[green]Reference[/]");
        resultTable.AddRow("Instance List", $"{balance4:C}", balance4 == balance3 ? "[green]Yes[/]" : "[red]No[/]");
        AnsiConsole.Write(resultTable);
        AnsiConsole.MarkupLine("[green]All four approaches produce identical results.[/]");
    }

    private static async Task<decimal> RunScenario(IHoldAllConfiguration config, string label)
    {
        var repo = new InMemoryEventSourcedRepository<string, BankAccountState>(config);
        using var session = await repo.BeginSessionFor($"{label}-001", throwIfNotExists: false);

        session.AddEvent(new AccountOpened("Test User", 1000m));
        session.AddEvent(new MoneyDeposited(500m, "Salary"));
        session.AddEvent(new MoneyWithdrawn(200m, "Rent"));

        var balance = session.GetCurrentState().Balance;
        await session.SaveChanges();

        AnsiConsole.MarkupLine($"  [{label}] Final balance: [green]{balance:C}[/]");
        AnsiConsole.WriteLine();

        return balance;
    }
}
