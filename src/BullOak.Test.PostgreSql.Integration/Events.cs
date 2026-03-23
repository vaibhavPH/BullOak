namespace BullOak.Test.PostgreSql.Integration;

// Test domain events
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

// Test state
public class AccountState
{
    public string AccountId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public decimal Balance { get; set; }
    public int TransactionCount { get; set; }
}
