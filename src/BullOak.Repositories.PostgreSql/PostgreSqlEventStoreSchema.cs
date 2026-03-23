namespace BullOak.Repositories.PostgreSql;

using System.Data;
using Dapper;
using Npgsql;

/// <summary>
/// Creates and manages the PostgreSQL schema for the event store.
/// All operations are idempotent — safe to call multiple times.
/// </summary>
public static class PostgreSqlEventStoreSchema
{
    public const string DefaultTableName = "events";

    private static string GetCreateTableSql(string tableName) => $@"
        CREATE TABLE IF NOT EXISTS {tableName} (
            global_position   BIGSERIAL       PRIMARY KEY,
            stream_id         TEXT            NOT NULL,
            stream_position   BIGINT          NOT NULL,
            event_type        TEXT            NOT NULL,
            event_data        JSONB           NOT NULL,
            created_at        TIMESTAMPTZ     NOT NULL DEFAULT now(),
            CONSTRAINT uq_{tableName}_stream_position UNIQUE (stream_id, stream_position)
        );

        CREATE INDEX IF NOT EXISTS ix_{tableName}_stream_id_position
            ON {tableName} (stream_id, stream_position);
    ";

    /// <summary>
    /// Creates the events table and indexes if they do not already exist.
    /// This is idempotent and safe to call on every application startup.
    /// </summary>
    public static async Task EnsureSchemaAsync(NpgsqlDataSource dataSource, string tableName = DefaultTableName)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(GetCreateTableSql(tableName));
    }

    /// <summary>
    /// Creates the events table and indexes if they do not already exist.
    /// This is idempotent and safe to call on every application startup.
    /// </summary>
    public static async Task EnsureSchemaAsync(string connectionString, string tableName = DefaultTableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(GetCreateTableSql(tableName));
    }
}
