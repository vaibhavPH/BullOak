using Npgsql;
using Testcontainers.PostgreSql;
using BullOak.Repositories.PostgreSql;

namespace BullOak.Test.ReadModel.Integration;

/// <summary>
/// Shared fixture that spins up a PostgreSQL container with BOTH schemas:
///   1. The BullOak event store schema (events table) — the write side
///   2. The read model schema (account_summary, transaction_history) — the read side
///
/// This models a real CQRS setup where events and read models live in the same
/// database (simplest deployment) or could be in separate databases (more scalable).
///
/// By having both schemas in one container, tests can demonstrate the full flow:
///   events table → projector → read model tables → SQL queries
/// </summary>
public class ReadModelFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public NpgsqlDataSource DataSource { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    public ReadModelFixture()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("bulloak_readmodel_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        DataSource = NpgsqlDataSource.Create(ConnectionString);

        // Create the event store schema (write side)
        await PostgreSqlEventStoreSchema.EnsureSchemaAsync(DataSource);

        // Create the read model schema (read side)
        await ReadModelSchema.EnsureSchemaAsync(DataSource);
    }

    public async Task DisposeAsync()
    {
        DataSource?.Dispose();
        await _container.DisposeAsync();
    }
}
