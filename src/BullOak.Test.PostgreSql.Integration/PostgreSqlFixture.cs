using Npgsql;
using Testcontainers.PostgreSql;
using BullOak.Repositories.PostgreSql;

namespace BullOak.Test.PostgreSql.Integration;

/// <summary>
/// Shared fixture that spins up a PostgreSQL container once for all tests in the collection.
/// Implements IAsyncLifetime so xUnit manages the container lifecycle automatically.
/// </summary>
public class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public NpgsqlDataSource DataSource { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    public PostgreSqlFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("bulloak_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        DataSource = NpgsqlDataSource.Create(ConnectionString);

        // Create the event store schema
        await PostgreSqlEventStoreSchema.EnsureSchemaAsync(DataSource);
    }

    public async Task DisposeAsync()
    {
        DataSource?.Dispose();
        await _container.DisposeAsync();
    }
}
