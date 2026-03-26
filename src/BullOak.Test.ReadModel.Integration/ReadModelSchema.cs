using Dapper;
using Npgsql;

namespace BullOak.Test.ReadModel.Integration;

/// <summary>
/// Manages the read model schema — the denormalized tables that are populated
/// by projecting events from the event store.
///
/// In CQRS, the write side stores events and the read side maintains query-optimized
/// views. These tables are the read side. They can always be rebuilt from scratch
/// by replaying events, so they are disposable and non-authoritative.
/// </summary>
public static class ReadModelSchema
{
    /// <summary>
    /// Creates the account_summary table — a denormalized view of account state.
    /// Each row represents the current state of one account, pre-computed from events.
    /// This is what you query when a user asks "what is my balance?"
    /// </summary>
    public const string CreateAccountSummarySql = @"
        CREATE TABLE IF NOT EXISTS account_summary (
            account_id    TEXT            PRIMARY KEY,
            owner_name    TEXT            NOT NULL,
            balance       DECIMAL(18,2)   NOT NULL DEFAULT 0,
            tx_count      INT             NOT NULL DEFAULT 0,
            last_updated  TIMESTAMPTZ     NOT NULL DEFAULT now()
        );
    ";

    /// <summary>
    /// Creates the transaction_history table — an append-only log of transactions.
    /// Each row is one event projected into a flat, queryable format.
    /// This is what you query when a user asks "show me my recent transactions."
    /// </summary>
    public const string CreateTransactionHistorySql = @"
        CREATE TABLE IF NOT EXISTS transaction_history (
            id            BIGSERIAL       PRIMARY KEY,
            account_id    TEXT            NOT NULL,
            event_type    TEXT            NOT NULL,
            amount        DECIMAL(18,2)   NOT NULL DEFAULT 0,
            description   TEXT            NOT NULL DEFAULT '',
            occurred_at   TIMESTAMPTZ     NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS ix_transaction_history_account_id
            ON transaction_history (account_id);
    ";

    /// <summary>
    /// Creates all read model tables. Idempotent — safe to call multiple times.
    /// </summary>
    public static async Task EnsureSchemaAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(CreateAccountSummarySql);
        await connection.ExecuteAsync(CreateTransactionHistorySql);
    }

    /// <summary>
    /// Drops and recreates all read model tables. Used to test the "rebuild from scratch" pattern.
    /// </summary>
    public static async Task ResetSchemaAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("DROP TABLE IF EXISTS account_summary CASCADE;");
        await connection.ExecuteAsync("DROP TABLE IF EXISTS transaction_history CASCADE;");
        await connection.ExecuteAsync(CreateAccountSummarySql);
        await connection.ExecuteAsync(CreateTransactionHistorySql);
    }
}
