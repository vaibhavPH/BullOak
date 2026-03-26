namespace BullOak.Test.ReadModel.Integration;

// Domain events for the bank account aggregate.
// These are the same events used by the PostgreSQL event store —
// they flow from the write side (event store) to the read side (read model).

public class AccountOpened
{
    public string AccountId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public decimal InitialDeposit { get; set; }
}

public class MoneyDeposited
{
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
}

public class MoneyWithdrawn
{
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
}

// Aggregate state used by BullOak's rehydration pipeline.
public class AccountState
{
    public string AccountId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public decimal Balance { get; set; }
    public int TransactionCount { get; set; }
}
