using DotNet.Testcontainers.Builders;
using EventStore.Client;
using Npgsql;
using Spectre.Console;
using Testcontainers.KurrentDb;
using Testcontainers.PostgreSql;
using BullOak.Repositories.PostgreSql;

namespace BullOak.Console.Infrastructure;

/// <summary>
/// Manages the lifecycle of PostgreSQL and EventStoreDB connections.
///
/// When UseTestContainers is true:
///   - Docker containers are started on first access
///   - They stay alive for the entire application lifetime
///   - Disposed when the application exits
///
/// When UseTestContainers is false:
///   - The connection string from appsettings.json is used directly
///   - No Docker containers are started
/// </summary>
public class InfrastructureManager : IAsyncDisposable
{
    private readonly InfrastructureSettings _settings;

    // PostgreSQL
    private PostgreSqlContainer? _pgContainer;
    private NpgsqlDataSource? _pgDataSource;
    private bool _pgInitialized;

    // EventStore
    private KurrentDbContainer? _esContainer;
    private EventStoreClient? _esClient;
    private string? _esConnectionString;
    private bool _esInitialized;

    public InfrastructureManager(InfrastructureSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Gets (and lazily initializes) the PostgreSQL NpgsqlDataSource.
    /// Starts a TestContainer if configured, otherwise uses the connection string.
    /// </summary>
    public async Task<NpgsqlDataSource> GetPostgreSqlDataSourceAsync()
    {
        if (_pgDataSource != null) return _pgDataSource;

        string connectionString;

        if (_settings.PostgreSql.UseTestContainers)
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Starting PostgreSQL TestContainer...[/]", async ctx =>
                {
                    _pgContainer = new PostgreSqlBuilder("postgres:latest")
                        .WithDatabase("bulloak_demo")
                        .WithUsername("demo")
                        .WithPassword("demo")
                        .WithCommand("postgres", "-c", "log_statement=all")
                        .Build();

                    await _pgContainer.StartAsync();
                });

            connectionString = _pgContainer!.GetConnectionString();
            AnsiConsole.MarkupLine($"[green]PostgreSQL container started.[/] Port: [aqua]{_pgContainer.GetMappedPublicPort(5432)}[/]");
        }
        else
        {
            connectionString = _settings.PostgreSql.ConnectionString;
            AnsiConsole.MarkupLine($"[green]Using PostgreSQL from appsettings.json[/]");
        }

        _pgDataSource = NpgsqlDataSource.Create(connectionString);

        // Ensure schema exists
        await PostgreSqlEventStoreSchema.EnsureSchemaAsync(_pgDataSource);
        AnsiConsole.MarkupLine("[dim]PostgreSQL schema ensured.[/]");

        _pgInitialized = true;
        return _pgDataSource;
    }

    /// <summary>
    /// Gets (and lazily initializes) the EventStoreDB client.
    /// Starts a TestContainer if configured, otherwise uses the connection string.
    /// </summary>
    public async Task<(EventStoreClient Client, string ConnectionString)> GetEventStoreClientAsync()
    {
        if (_esClient != null) return (_esClient, _esConnectionString!);

        string connectionString;

        if (_settings.EventStore.UseTestContainers)
        {
            AnsiConsole.MarkupLine("[yellow]Starting EventStoreDB TestContainer (this may take up to 60s)...[/]");

            _esContainer = new KurrentDbBuilder("eventstore/eventstore:latest")
                .WithPortBinding(KurrentDbBuilder.KurrentDbPort, false)
                .WithEnvironment("EVENTSTORE_INSECURE", "true")
                .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
                .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "All")
                .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "true")
                .WithEnvironment("EVENTSTORE_MEM_DB", "true")
                .WithEnvironment("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP","true")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r
                        .ForPort(KurrentDbBuilder.KurrentDbPort)
                        .ForPath("/health/live")
                        .ForStatusCode(System.Net.HttpStatusCode.NoContent)))
                .Build();

            await _esContainer.StartAsync();

            var host = _esContainer.Hostname;
            var port = _esContainer.GetMappedPublicPort(KurrentDbBuilder.KurrentDbPort);
            connectionString = $"esdb://{host}:{port}?tls=false";
            AnsiConsole.MarkupLine($"[green]EventStoreDB container started.[/] Port: [aqua]{port}[/]");
        }
        else
        {
            connectionString = _settings.EventStore.ConnectionString;
            AnsiConsole.MarkupLine($"[green]Using EventStoreDB from appsettings.json[/]");
        }

        var settings = EventStoreClientSettings.Create(connectionString);
        _esClient = new EventStoreClient(settings);
        _esConnectionString = connectionString;

        _esInitialized = true;
        return (_esClient, connectionString);
    }

    /// <summary>
    /// Whether PostgreSQL infrastructure has been initialized in this session.
    /// </summary>
    public bool IsPostgreSqlInitialized => _pgInitialized;

    /// <summary>
    /// Whether EventStoreDB infrastructure has been initialized in this session.
    /// </summary>
    public bool IsEventStoreInitialized => _esInitialized;

    /// <summary>
    /// Describes the current configuration for display purposes.
    /// </summary>
    public string GetPostgreSqlMode() =>
        _settings.PostgreSql.UseTestContainers ? "TestContainers (Docker)" : "appsettings.json";

    public string GetEventStoreMode() =>
        _settings.EventStore.UseTestContainers ? "TestContainers (Docker)" : "appsettings.json";

    public async ValueTask DisposeAsync()
    {
        _pgDataSource?.Dispose();

        _esClient?.Dispose();

        if (_pgContainer != null)
        {
            AnsiConsole.MarkupLine("[dim]Stopping PostgreSQL container...[/]");
            await _pgContainer.DisposeAsync();
        }

        if (_esContainer != null)
        {
            AnsiConsole.MarkupLine("[dim]Stopping EventStoreDB container...[/]");
            await _esContainer.DisposeAsync();
        }
    }
}
