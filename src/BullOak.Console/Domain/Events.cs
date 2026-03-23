namespace BullOak.Console.Domain;

// ─────────────────────────────────────────────────────────────
//  Bank Account Events
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Raised when a new bank account is opened.
/// </summary>
public record AccountOpened(string AccountHolder, decimal InitialDeposit);

/// <summary>
/// Raised when money is deposited into an account.
/// </summary>
public record MoneyDeposited(decimal Amount, string Description);

/// <summary>
/// Raised when money is withdrawn from an account.
/// </summary>
public record MoneyWithdrawn(decimal Amount, string Description);

/// <summary>
/// Raised when an account is frozen (e.g., fraud detection).
/// </summary>
public record AccountFrozen(string Reason);

/// <summary>
/// Raised when an account is unfrozen.
/// </summary>
public record AccountUnfrozen();

// ─────────────────────────────────────────────────────────────
//  Upconversion Events (Schema Evolution)
//  Demonstrates migrating from V1 → V2 → V3 of an event
// ─────────────────────────────────────────────────────────────

/// <summary>
/// V1: Original event — only stored the amount.
/// This is the "legacy" event that was persisted long ago.
/// </summary>
public record MoneyDepositedV1(decimal Amount);

/// <summary>
/// V2: Added a description field.
/// V1 events are upconverted to V2 by adding a default description.
/// </summary>
public record MoneyDepositedV2(decimal Amount, string Description);

/// <summary>
/// V3 (current): Added a timestamp and currency.
/// V2 events are upconverted to V3 by adding defaults.
/// </summary>
public record MoneyDepositedV3(decimal Amount, string Description, DateTime Timestamp, string Currency);

// ─────────────────────────────────────────────────────────────
//  Cinema Reservation Events (second domain example)
// ─────────────────────────────────────────────────────────────

/// <summary>
/// A seat was reserved in a cinema screening.
/// </summary>
public record SeatReserved(string SeatNumber, string CustomerName);

/// <summary>
/// A seat reservation was cancelled.
/// </summary>
public record SeatCancelled(string SeatNumber);
