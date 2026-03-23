namespace BullOak.Repositories.PostgreSql;

using System.Data;
using Dapper;
using Npgsql;
using BullOak.Repositories.Appliers;
using BullOak.Repositories.Exceptions;
using BullOak.Repositories.Session;
using BullOak.Repositories.PostgreSql.Serialization;

/// <summary>
/// Event store session backed by PostgreSQL. Extends BaseEventSourcedSession to inherit
/// BullOak's rehydration, publishing, interception, and validation logic.
///
/// Thread safety: Sessions are NOT thread-safe (same as the InMemory implementation).
/// Each logical operation should use its own session via BeginSessionFor().
///
/// Connection lifecycle: The session does not hold an open connection. It opens one
/// only during SaveChanges, within a using block. NpgsqlDataSource handles pooling.
/// </summary>
internal class PostgreSqlEventStoreSession<TState> : BaseEventSourcedSession<TState>
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _streamId;
    private readonly string _tableName;
    private readonly IEventSerializer _serializer;
    private long _expectedNextPosition;

    public PostgreSqlEventStoreSession(
        IHoldAllConfiguration configuration,
        NpgsqlDataSource dataSource,
        string streamId,
        IEventSerializer serializer,
        string tableName = PostgreSqlEventStoreSchema.DefaultTableName)
        : base(configuration)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _streamId = streamId ?? throw new ArgumentNullException(nameof(streamId));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _tableName = tableName;
    }

    public PostgreSqlEventStoreSession(
        IValidateState<TState> stateValidator,
        IHoldAllConfiguration configuration,
        NpgsqlDataSource dataSource,
        string streamId,
        IEventSerializer serializer,
        string tableName = PostgreSqlEventStoreSchema.DefaultTableName)
        : base(stateValidator, configuration)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _streamId = streamId ?? throw new ArgumentNullException(nameof(streamId));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _tableName = tableName;
    }

    /// <summary>
    /// Sets the expected next position based on the loaded events.
    /// Called by the repository after LoadFromEvents.
    /// </summary>
    internal void SetExpectedNextPosition(long position)
    {
        _expectedNextPosition = position;
    }

    /// <inheritdoc />
    protected override async Task<int> SaveChanges(
        ItemWithType[] newEvents,
        TState currentState,
        CancellationToken? cancellationToken)
    {
        if (newEvents == null || newEvents.Length == 0)
            return 0;

        var ct = cancellationToken ?? CancellationToken.None;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            var sql = $@"
                INSERT INTO {_tableName} (stream_id, stream_position, event_type, event_data)
                VALUES (@StreamId, @StreamPosition, @EventType, @EventData::jsonb)";

            for (int i = 0; i < newEvents.Length; i++)
            {
                var @event = newEvents[i];
                var position = _expectedNextPosition + i;

                var parameters = new
                {
                    StreamId = _streamId,
                    StreamPosition = position,
                    EventType = _serializer.GetTypeName(@event.type),
                    EventData = _serializer.Serialize(@event.instance, @event.type)
                };

                await connection.ExecuteAsync(
                    new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
            }

            await transaction.CommitAsync(ct);

            _expectedNextPosition += newEvents.Length;
            return (int)_expectedNextPosition;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(ct);
            throw new ConcurrencyException(_streamId, ex);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
