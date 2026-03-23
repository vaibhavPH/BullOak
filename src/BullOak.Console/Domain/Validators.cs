using BullOak.Repositories.Session;

namespace BullOak.Console.Domain;

// ─────────────────────────────────────────────────────────────
//  State Validators
//
//  Implement IValidateState<TState> to add business rule
//  validation. BullOak calls Validate() before SaveChanges().
//  If validation fails, a BusinessException is thrown and the
//  events are NOT persisted.
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Validates BankAccountState business rules:
/// - Balance must not go negative (no overdraft).
/// </summary>
public class BankAccountValidator : IValidateState<BankAccountState>
{
    public ValidationResults Validate(BankAccountState state)
    {
        var errors = new List<IValidationError>();

        if (state.Balance < 0)
            errors.Add(new BasicValidationError("Account balance cannot be negative. Insufficient funds."));

        return errors.Count > 0
            ? ValidationResults.Errors(errors)
            : ValidationResults.Success();
    }
}
