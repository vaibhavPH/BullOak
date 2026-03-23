using BullOak.Repositories.Appliers;

namespace BullOak.Test.PostgreSql.Integration;

public class AccountOpenedApplier : BaseApplyEvents<AccountState, AccountOpened>
{
    public override AccountState Apply(AccountState state, AccountOpened @event)
    {
        state.AccountId = @event.AccountId;
        state.OwnerName = @event.OwnerName;
        state.Balance = @event.InitialDeposit;
        state.TransactionCount = 1;
        return state;
    }
}

public class MoneyDepositedApplier : BaseApplyEvents<AccountState, MoneyDeposited>
{
    public override AccountState Apply(AccountState state, MoneyDeposited @event)
    {
        state.Balance += @event.Amount;
        state.TransactionCount++;
        return state;
    }
}

public class MoneyWithdrawnApplier : BaseApplyEvents<AccountState, MoneyWithdrawn>
{
    public override AccountState Apply(AccountState state, MoneyWithdrawn @event)
    {
        state.Balance -= @event.Amount;
        state.TransactionCount++;
        return state;
    }
}
