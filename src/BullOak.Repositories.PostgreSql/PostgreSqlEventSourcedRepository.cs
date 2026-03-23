namespace BullOak.Repositories.PostgreSql;

using Dapper;
using Npgsql;
using BullOak.Repositories.Appliers;
using BullOak.Repositories.Exceptions;
using BullOak.Repositories.Repository;
using BullOak.Repositories.Session;
using BullOak.Repositories.PostgreSql.Serialization;

/// <summary>
/// PostgreSQL-backed event sourced repository. Creates sessions that read/write events
/// to a PostgreSQL database using Dapper for data access.
///
/// Thread safety: The repository is safe for concurrent use from multiple threads.
/// NpgsqlDataSource handles connection pooling internally. Each BeginSessionFor call
/// creates an independent session with its own state.
///
/// Connection lifecycle: Connections are borrowed from the pool only during database
/// operations and returned immediately. A session holds zero database resources between
/// load and save, which is safe for long-lived sessions (e.g., user think time).
/// </summary>
public class PostgreSqlEventSourcedRepository<TState> : IStartSessions<string, TState>
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IHoldAllConfiguration _configuration;
    private readonly IValidateState<TState>? _stateValidator;
    private readonly IEventSerializer _serializer;
    private readonly string _tableName;

    public PostgreSqlEventSourcedRepository(
        IHoldAllConfiguration configuration,
        NpgsqlDataSource dataSource,
        IEventSerializer? serializer = null,
        string tableName = PostgreSqlEventStoreSchema.DefaultTableName)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _serializer = serializer ?? new JsonEventSerializer();
        _tableName = tableName;
    }

    public PostgreSqlEventSourcedRepository(
        IHoldAllConfiguration configuration,
        string connectionString,
        IEventSerializer? serializer = null,
        string tableName = PostgreSqlEventStoreSchema.DefaultTableName)
        : this(configuration, NpgsqlDataSource.Create(connectionString), serializer, tableName)
    {
    }

    public PostgreSqlEventSourcedRepository(
        IValidateState<TState> stateValidator,
        IHoldAllConfiguration configuration,
        NpgsqlDataSource dataSource,
        IEventSerializer? serializer = null,
        string tableName = PostgreSqlEventStoreSchema.DefaultTableName)
        : this(configuration, dataSource, serializer, tableName)
    {
        _stateValidator = stateValidator ?? throw new ArgumentNullException(nameof(stateValidator));
    }

    public async Task<IManageSessionOf<TState>> BeginSessionFor(
        string selector,
        bool throwIfNotExists = false,
        DateTime? appliesAt = null)
    {
        if (string.IsNullOrEmpty(selector))
            throw new ArgumentException("Stream ID cannot be null or empty.", nameof(selector));

        var session = _stateValidator != null
            ? new PostgreSqlEventStoreSession<TState>(
                _stateValidator, _configuration, _dataSource, selector, _serializer, _tableName)
            : new PostgreSqlEventStoreSession<TState>(
                _configuration, _dataSource, selector, _serializer, _tableName);

        var storedEvents = await ReadEventsAsync(selector, appliesAt);

        if (storedEvents.Length == 0 && throwIfNotExists)
            throw new StreamNotFoundException(selector);

        session.LoadFromEvents(storedEvents);

        // Set expected next position for optimistic concurrency.
        // ConcurrencyId is the index of the last event (-1 for empty streams).
        session.SetExpectedNextPosition(session.ConcurrencyId + 1);

        return session;
    }

    public async Task Delete(string selector)
    {
        var sql = $"DELETE FROM {_tableName} WHERE stream_id = @StreamId";
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, new { StreamId = selector });
    }

    public async Task<bool> Contains(string selector)
    {
        var sql = $"SELECT EXISTS(SELECT 1 FROM {_tableName} WHERE stream_id = @StreamId)";
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<bool>(sql, new { StreamId = selector });
    }

    private async Task<StoredEvent[]> ReadEventsAsync(string streamId, DateTime? appliesAt)
    {
        string sql;
        object parameters;

        if (appliesAt.HasValue)
        {
            sql = $@"
                SELECT event_type, event_data, stream_position
                FROM {_tableName}
                WHERE stream_id = @StreamId AND created_at <= @AppliesAt
                ORDER BY stream_position ASC";
            parameters = new { StreamId = streamId, AppliesAt = appliesAt.Value };
        }
        else
        {
            sql = $@"
                SELECT event_type, event_data, stream_position
                FROM {_tableName}
                WHERE stream_id = @StreamId
                ORDER BY stream_position ASC";
            parameters = new { StreamId = streamId };
        }

        await using var connection = await _dataSource.OpenConnectionAsync();
        var rows = await connection.QueryAsync<EventRow>(sql, parameters);

        return rows.Select(row =>
        {
            var eventType = _serializer.GetTypeFromName(row.event_type);
            if (eventType == null)
                throw new InvalidOperationException(
                    $"Cannot resolve event type '{row.event_type}'. " +
                    "Ensure the assembly containing this type is loaded.");

            var eventObj = _serializer.Deserialize(row.event_data, eventType);
            return new StoredEvent(eventType, eventObj, row.stream_position);
        }).ToArray();
    }

    /// <summary>
    /// Internal DTO for Dapper to map database rows.
    /// Column names match the PostgreSQL schema exactly (snake_case).
    /// </summary>
    private class EventRow
    {
        public string event_type { get; set; } = "";
        public string event_data { get; set; } = "";
        public long stream_position { get; set; }
    }
}
