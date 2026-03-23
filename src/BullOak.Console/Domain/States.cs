namespace BullOak.Console.Domain;

// ─────────────────────────────────────────────────────────────
//  Concrete (class-based) State
//  Used with Activator or default state factory.
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Represents the current state of a bank account.
/// BullOak applies events to this state through event appliers.
/// Each property gets updated as events are replayed.
/// </summary>
public class BankAccountState
{
    public string AccountHolder { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public bool IsFrozen { get; set; }
    public int TransactionCount { get; set; }
}

// ─────────────────────────────────────────────────────────────
//  Interface-based State
//  BullOak dynamically emits a class implementing this interface
//  at runtime. This enables read-only locking and is the
//  recommended approach for production use.
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Interface-based state for a bank account.
/// BullOak generates an implementation at runtime using IL emission.
/// The generated type supports ICanSwitchBackAndToReadOnly for
/// thread-safe read-only locking after rehydration.
/// </summary>
public interface IBankAccountState
{
    string AccountHolder { get; set; }
    decimal Balance { get; set; }
    bool IsFrozen { get; set; }
    int TransactionCount { get; set; }
}

// ─────────────────────────────────────────────────────────────
//  Cinema State (second domain)
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Tracks which seats are reserved in a cinema screening.
/// </summary>
public class CinemaScreeningState
{
    public Dictionary<string, string> ReservedSeats { get; set; } = new();
    public int TotalReservations { get; set; }
}
