using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.Exceptions;
using BullOak.Repositories.InMemory;
using BullOak.Repositories.Session;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 8: State Validation
///
/// Validators check business rules before events are persisted.
/// If validation fails, SaveChanges throws BusinessException and
/// no events are saved to the store.
///
/// Key types:
///   - IValidateState&lt;TState&gt;: Interface to implement
///   - ValidationResults: Success() or Errors(list)
///   - IValidationError / BasicValidationError
///   - BusinessException: Thrown on validation failure
/// </summary>
public static class ValidationDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 8: State Validation[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            "[bold]public class BankAccountValidator : IValidateState<BankAccountState>[/]\n" +
            "[bold]{[/]\n" +
            "[bold]    public ValidationResults Validate(BankAccountState state)[/]\n" +
            "[bold]    {[/]\n" +
            "[bold]        var errors = new List<IValidationError>();[/]\n" +
            "[bold]        if (state.Balance < 0)[/]\n" +
            "[bold]            errors.Add(new BasicValidationError(\"Insufficient funds.\"));[/]\n" +
            "[bold]        return errors.Count > 0[/]\n" +
            "[bold]            ? ValidationResults.Errors(errors)[/]\n" +
            "[bold]            : ValidationResults.Success();[/]\n" +
            "[bold]    }[/]\n" +
            "[bold]}[/]\n\n" +
            "[dim]Pass the validator to the repository constructor.\n" +
            "BullOak calls Validate() automatically during SaveChanges().\n" +
            "If it returns errors, a BusinessException is thrown.[/]")
            .Header("[yellow]Validator Implementation[/]")
            .Border(BoxBorder.Rounded));

        var config = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(AccountOpenedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        // Pass validator to repository constructor
        var validator = new BankAccountValidator();
        var repo = new InMemoryEventSourcedRepository<string, BankAccountState>(validator, config);

        // ── Scenario 1: Valid transaction ──────────────────────
        AnsiConsole.MarkupLine("[bold yellow]Scenario 1: Valid transaction (deposit)[/]");
        using (var session = await repo.BeginSessionFor("VAL-001", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Validated User", 1000m));
            session.AddEvent(new MoneyDeposited(500m, "Salary"));

            var state = session.GetCurrentState();
            AnsiConsole.MarkupLine($"  Balance before save: [green]{state.Balance:C}[/]");

            int saved = await session.SaveChanges();
            AnsiConsole.MarkupLine($"  [green]Saved {saved} events successfully — validation passed.[/]");
        }
        AnsiConsole.WriteLine();

        // ── Scenario 2: Invalid transaction (overdraft) ────────
        AnsiConsole.MarkupLine("[bold yellow]Scenario 2: Invalid transaction (overdraft)[/]");
        using (var session = await repo.BeginSessionFor("VAL-002", throwIfNotExists: false))
        {
            session.AddEvent(new AccountOpened("Overdraft User", 100m));
            session.AddEvent(new MoneyWithdrawn(500m, "Trying to withdraw too much"));

            var state = session.GetCurrentState();
            AnsiConsole.MarkupLine($"  Balance before save: [red]{state.Balance:C}[/] (negative!)");

            try
            {
                await session.SaveChanges();
                AnsiConsole.MarkupLine("[red]  ERROR: Should have thrown![/]");
            }
            catch (BusinessException ex)
            {
                AnsiConsole.MarkupLine($"  [green]BusinessException caught:[/] [red]{ex.Message}[/]");
                AnsiConsole.MarkupLine("  [dim]Events were NOT saved to the store.[/]");
            }
        }
        AnsiConsole.WriteLine();

        // ── Scenario 3: Verify rejected events weren't persisted
        AnsiConsole.MarkupLine("[bold yellow]Scenario 3: Verify rejected events were not persisted[/]");
        bool exists = await repo.Contains("VAL-002");
        AnsiConsole.MarkupLine($"  Stream 'VAL-002' exists in store: [aqua]{exists}[/]");
        AnsiConsole.MarkupLine("  [dim]The stream has no events because validation rejected them.[/]");
        AnsiConsole.WriteLine();

        // Summary
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Validation Component[/]");
        table.AddColumn("[bold]Purpose[/]");
        table.AddRow("[aqua]IValidateState<T>[/]", "Interface to implement with Validate(state) method");
        table.AddRow("[aqua]ValidationResults[/]", "Return type: Success() or Errors(list)");
        table.AddRow("[aqua]BasicValidationError[/]", "Simple error with message (implicit from string)");
        table.AddRow("[aqua]BusinessException[/]", "Thrown when validation fails during SaveChanges");
        table.AddRow("[aqua]AlwaysPassValidator<T>[/]", "Default no-op validator (all states valid)");
        AnsiConsole.Write(table);
    }
}
