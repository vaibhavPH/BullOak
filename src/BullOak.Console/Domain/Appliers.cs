using BullOak.Repositories.Appliers;

namespace BullOak.Console.Domain;

// ─────────────────────────────────────────────────────────────
//  Class-based Appliers (implement IApplyEvent<TState, TEvent>)
//
//  These are discovered automatically when using
//  WithAnyAppliersFrom(assembly) or WithAnyAppliersFromInstances().
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Applies AccountOpened events to BankAccountState.
/// Implements the typed IApplyEvent interface so BullOak
/// can auto-discover it via reflection.
/// </summary>
public class AccountOpenedApplier : IApplyEvent<BankAccountState, AccountOpened>
{
    public BankAccountState Apply(BankAccountState state, AccountOpened @event)
    {
        state.AccountHolder = @event.AccountHolder;
        state.Balance = @event.InitialDeposit;
        state.TransactionCount = 1;
        return state;
    }
}

/// <summary>
/// Applies MoneyDeposited events to BankAccountState.
/// </summary>
public class MoneyDepositedApplier : IApplyEvent<BankAccountState, MoneyDeposited>
{
    public BankAccountState Apply(BankAccountState state, MoneyDeposited @event)
    {
        state.Balance += @event.Amount;
        state.TransactionCount++;
        return state;
    }
}

/// <summary>
/// Applies MoneyWithdrawn events to BankAccountState.
/// </summary>
public class MoneyWithdrawnApplier : IApplyEvent<BankAccountState, MoneyWithdrawn>
{
    public BankAccountState Apply(BankAccountState state, MoneyWithdrawn @event)
    {
        state.Balance -= @event.Amount;
        state.TransactionCount++;
        return state;
    }
}

/// <summary>
/// Applies AccountFrozen events.
/// </summary>
public class AccountFrozenApplier : IApplyEvent<BankAccountState, AccountFrozen>
{
    public BankAccountState Apply(BankAccountState state, AccountFrozen @event)
    {
        state.IsFrozen = true;
        return state;
    }
}

/// <summary>
/// Applies AccountUnfrozen events.
/// </summary>
public class AccountUnfrozenApplier : IApplyEvent<BankAccountState, AccountUnfrozen>
{
    public BankAccountState Apply(BankAccountState state, AccountUnfrozen @event)
    {
        state.IsFrozen = false;
        return state;
    }
}

// ─────────────────────────────────────────────────────────────
//  Interface-based State Appliers
//  Same events, but applied to IBankAccountState (interface).
// ─────────────────────────────────────────────────────────────

public class InterfaceAccountOpenedApplier : IApplyEvent<IBankAccountState, AccountOpened>
{
    public IBankAccountState Apply(IBankAccountState state, AccountOpened @event)
    {
        state.AccountHolder = @event.AccountHolder;
        state.Balance = @event.InitialDeposit;
        state.TransactionCount = 1;
        return state;
    }
}

public class InterfaceMoneyDepositedApplier : IApplyEvent<IBankAccountState, MoneyDeposited>
{
    public IBankAccountState Apply(IBankAccountState state, MoneyDeposited @event)
    {
        state.Balance += @event.Amount;
        state.TransactionCount++;
        return state;
    }
}

public class InterfaceMoneyWithdrawnApplier : IApplyEvent<IBankAccountState, MoneyWithdrawn>
{
    public IBankAccountState Apply(IBankAccountState state, MoneyWithdrawn @event)
    {
        state.Balance -= @event.Amount;
        state.TransactionCount++;
        return state;
    }
}

public class InterfaceAccountFrozenApplier : IApplyEvent<IBankAccountState, AccountFrozen>
{
    public IBankAccountState Apply(IBankAccountState state, AccountFrozen @event)
    {
        state.IsFrozen = true;
        return state;
    }
}

public class InterfaceAccountUnfrozenApplier : IApplyEvent<IBankAccountState, AccountUnfrozen>
{
    public IBankAccountState Apply(IBankAccountState state, AccountUnfrozen @event)
    {
        state.IsFrozen = false;
        return state;
    }
}

// ─────────────────────────────────────────────────────────────
//  Upconversion Applier (V3 is the current version)
// ─────────────────────────────────────────────────────────────

public class MoneyDepositedV3Applier : IApplyEvent<BankAccountState, MoneyDepositedV3>
{
    public BankAccountState Apply(BankAccountState state, MoneyDepositedV3 @event)
    {
        state.Balance += @event.Amount;
        state.TransactionCount++;
        return state;
    }
}

// ─────────────────────────────────────────────────────────────
//  Cinema Appliers
// ─────────────────────────────────────────────────────────────

public class SeatReservedApplier : IApplyEvent<CinemaScreeningState, SeatReserved>
{
    public CinemaScreeningState Apply(CinemaScreeningState state, SeatReserved @event)
    {
        state.ReservedSeats[@event.SeatNumber] = @event.CustomerName;
        state.TotalReservations++;
        return state;
    }
}

public class SeatCancelledApplier : IApplyEvent<CinemaScreeningState, SeatCancelled>
{
    public CinemaScreeningState Apply(CinemaScreeningState state, SeatCancelled @event)
    {
        state.ReservedSeats.Remove(@event.SeatNumber);
        return state;
    }
}
