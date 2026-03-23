using BullOak.Repositories.Upconverting;

namespace BullOak.Console.Domain;

// ─────────────────────────────────────────────────────────────
//  Upconverters (Schema Evolution / Event Versioning)
//
//  When your event schema changes over time, old events stored
//  in the database need to be transformed into the current
//  version before being applied. BullOak handles this through
//  the IUpconvertEvent<TSource, TDestination> interface.
//
//  The chain: V1 → V2 → V3
//  BullOak recursively applies upconverters so a V1 event
//  gets converted to V2, then V2 to V3 automatically.
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Upconverts MoneyDepositedV1 (legacy) → MoneyDepositedV2.
/// Adds a default description since V1 didn't have one.
/// </summary>
public class MoneyDepositedV1ToV2 : IUpconvertEvent<MoneyDepositedV1, MoneyDepositedV2>
{
    public MoneyDepositedV2 Upconvert(MoneyDepositedV1 source)
        => new(source.Amount, "Legacy deposit (no description)");
}

/// <summary>
/// Upconverts MoneyDepositedV2 → MoneyDepositedV3 (current).
/// Adds timestamp and currency defaults.
/// </summary>
public class MoneyDepositedV2ToV3 : IUpconvertEvent<MoneyDepositedV2, MoneyDepositedV3>
{
    public MoneyDepositedV3 Upconvert(MoneyDepositedV2 source)
        => new(source.Amount, source.Description, DateTime.UtcNow, "GBP");
}
