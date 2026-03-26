using Dapper;
using Npgsql;

namespace BullOak.Test.ReadModel.Integration;

/// <summary>
/// Projects domain events into denormalized read model tables.
///
/// This is the bridge between the event-sourced write side and the relational read side.
/// Each method takes a domain event and updates the appropriate read model table(s).
///
/// In a production system, this projector would typically:
///   1. Subscribe to the event store (or a message bus)
///   2. Receive events as they are written
///   3. Update read model tables incrementally
///
/// Key principles:
///   - Projections are idempotent where possible (UPSERT pattern)
///   - Read models are disposable — they can be rebuilt by replaying all events
///   - Each event can project into multiple tables (one event → many read models)
/// </summary>
public class AccountReadModelProjector
{
    private readonly NpgsqlDataSource _dataSource;

    public AccountReadModelProjector(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    /// <summary>
    /// Projects an AccountOpened event into both read model tables.
    /// - account_summary: creates a new row with initial balance
    /// - transaction_history: records the opening transaction
    /// </summary>
    public async Task ProjectAccountOpened(AccountOpened @event)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();

        // Upsert into account_summary — handles replays gracefully
        await connection.ExecuteAsync(@"
            INSERT INTO account_summary (account_id, owner_name, balance, tx_count, last_updated)
            VALUES (@AccountId, @OwnerName, @InitialDeposit, 1, now())
            ON CONFLICT (account_id)
            DO UPDATE SET owner_name = @OwnerName,
                          balance = @InitialDeposit,
                          tx_count = 1,
                          last_updated = now();
        ", new { @event.AccountId, @event.OwnerName, @event.InitialDeposit });

        // Append to transaction_history
        await connection.ExecuteAsync(@"
            INSERT INTO transaction_history (account_id, event_type, amount, description, occurred_at)
            VALUES (@AccountId, 'AccountOpened', @InitialDeposit, 'Account opened', now());
        ", new { @event.AccountId, @event.InitialDeposit });
    }

    /// <summary>
    /// Projects a MoneyDeposited event — updates the summary balance and records the transaction.
    /// </summary>
    public async Task ProjectMoneyDeposited(string accountId, MoneyDeposited @event)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(@"
            UPDATE account_summary
            SET balance = balance + @Amount,
                tx_count = tx_count + 1,
                last_updated = now()
            WHERE account_id = @AccountId;
        ", new { AccountId = accountId, @event.Amount });

        await connection.ExecuteAsync(@"
            INSERT INTO transaction_history (account_id, event_type, amount, description, occurred_at)
            VALUES (@AccountId, 'MoneyDeposited', @Amount, @Description, now());
        ", new { AccountId = accountId, @event.Amount, @event.Description });
    }

    /// <summary>
    /// Projects a MoneyWithdrawn event — decreases the summary balance and records the transaction.
    /// </summary>
    public async Task ProjectMoneyWithdrawn(string accountId, MoneyWithdrawn @event)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(@"
            UPDATE account_summary
            SET balance = balance - @Amount,
                tx_count = tx_count + 1,
                last_updated = now()
            WHERE account_id = @AccountId;
        ", new { AccountId = accountId, @event.Amount });

        await connection.ExecuteAsync(@"
            INSERT INTO transaction_history (account_id, event_type, amount, description, occurred_at)
            VALUES (@AccountId, 'MoneyWithdrawn', @Amount, @Description, now());
        ", new { AccountId = accountId, @event.Amount, @event.Description });
    }

    /// <summary>
    /// Dispatches any event to the appropriate projection method.
    /// This is the entry point when processing events from a stream or subscription.
    /// </summary>
    public async Task ProjectEvent(string accountId, object @event)
    {
        switch (@event)
        {
            case AccountOpened opened:
                await ProjectAccountOpened(opened);
                break;
            case MoneyDeposited deposited:
                await ProjectMoneyDeposited(accountId, deposited);
                break;
            case MoneyWithdrawn withdrawn:
                await ProjectMoneyWithdrawn(accountId, withdrawn);
                break;
        }
    }
}
