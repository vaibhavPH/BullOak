namespace BullOak.Test.RabbitMq.Integration;

// Domain events for the bank account aggregate.
// MassTransit uses these types for message routing — the type name
// becomes the exchange/queue name in RabbitMQ by convention.

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

// Aggregate state for BullOak rehydration
public class AccountState
{
    public string AccountId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public decimal Balance { get; set; }
    public int TransactionCount { get; set; }
}
