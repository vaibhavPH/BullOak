using System.Reflection;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.PostgreSql;
using Dapper;
using FluentAssertions;

namespace BullOak.Test.ReadModel.Integration;

/// <summary>
/// Integration tests demonstrating the CQRS read model projection pattern.
///
/// In CQRS (Command Query Responsibility Segregation):
///   - The WRITE side stores events in an append-only event store
///   - The READ side maintains denormalized views (read models) optimized for queries
///   - A PROJECTOR bridges the two: it reads events and updates read model tables
///
/// Why separate read models?
///   - Event stores are optimized for writes (append-only, per-stream)
///   - Queries like "find all accounts with balance > 1000" require table scans across all streams
///   - Read models are normal SQL tables — you can index, join, aggregate, and paginate freely
///   - One event stream can feed MULTIPLE read models (e.g., account summary + transaction history)
///
/// The read model is DISPOSABLE: you can always delete it and rebuild from events.
/// This is a key insight — the event store is the source of truth, not the read model.
///
/// These tests use a real PostgreSQL container to validate the full flow:
///   BullOak session → event store (events table) → projector → read model tables → SQL queries
/// </summary>
[Collection("ReadModel")]
public class ReadModelProjectionTests
{
    private readonly ReadModelFixture _fixture;
    private readonly IHoldAllConfiguration _configuration;

    public ReadModelProjectionTests(ReadModelFixture fixture)
    {
        _fixture = fixture;
        _configuration = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(Assembly.GetExecutingAssembly())
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();
    }

    private PostgreSqlEventSourcedRepository<AccountState> CreateRepo()
        => new(_configuration, _fixture.DataSource);

    private AccountReadModelProjector CreateProjector()
        => new(_fixture.DataSource);

    private string UniqueStreamId() => $"account-{Guid.NewGuid()}";

    /// <summary>
    /// The simplest projection test: save an event through BullOak, project it
    /// into the read model, and query the result with plain SQL.
    ///
    /// This demonstrates the fundamental CQRS flow:
    ///   1. Write: BullOak persists AccountOpened to the events table
    ///   2. Project: The projector reads the event and INSERTs into account_summary
    ///   3. Query: Standard SQL returns the denormalized row
    ///
    /// Notice how the read model row has pre-computed values (balance, owner_name)
    /// that would otherwise require replaying events to derive.
    /// </summary>
    [Fact]
    public async Task ProjectEventToReadModel_ShouldCreateDenormalizedRow()
    {
        var repo = CreateRepo();
        var projector = CreateProjector();
        var streamId = UniqueStreamId();

        // WRITE: persist event through BullOak
        var openedEvent = new AccountOpened
        {
            AccountId = streamId,
            OwnerName = "Alice",
            InitialDeposit = 500m
        };

        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(openedEvent);
            await session.SaveChanges();
        }

        // PROJECT: transform event into read model
        await projector.ProjectAccountOpened(openedEvent);

        // QUERY: read the denormalized data with plain SQL
        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        var summary = await connection.QuerySingleAsync<AccountSummaryRow>(
            "SELECT account_id, owner_name, balance, tx_count FROM account_summary WHERE account_id = @Id",
            new { Id = streamId });

        summary.account_id.Should().Be(streamId);
        summary.owner_name.Should().Be("Alice");
        summary.balance.Should().Be(500m);
        summary.tx_count.Should().Be(1);
    }

    /// <summary>
    /// Demonstrates incremental projection — each new event updates the read model
    /// row rather than rebuilding it from scratch.
    ///
    /// This is how projections work in production:
    ///   - The projector processes events one at a time
    ///   - Each event modifies the read model incrementally (UPDATE ... SET balance = balance + @Amount)
    ///   - The read model always reflects the latest state
    ///
    /// The balance after 3 events: 1000 + 250 - 75 = 1175
    /// The transaction count: 3 (one per event)
    /// </summary>
    [Fact]
    public async Task MultipleEvents_ShouldUpdateReadModelIncrementally()
    {
        var repo = CreateRepo();
        var projector = CreateProjector();
        var streamId = UniqueStreamId();

        // Write events through BullOak
        var openedEvent = new AccountOpened
        {
            AccountId = streamId,
            OwnerName = "Bob",
            InitialDeposit = 1000m
        };
        var depositEvent = new MoneyDeposited { Amount = 250m, Description = "Salary" };
        var withdrawEvent = new MoneyWithdrawn { Amount = 75m, Description = "Groceries" };

        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(openedEvent);
            session.AddEvent(depositEvent);
            session.AddEvent(withdrawEvent);
            await session.SaveChanges();
        }

        // Project each event incrementally
        await projector.ProjectAccountOpened(openedEvent);
        await projector.ProjectMoneyDeposited(streamId, depositEvent);
        await projector.ProjectMoneyWithdrawn(streamId, withdrawEvent);

        // Query the result
        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        var summary = await connection.QuerySingleAsync<AccountSummaryRow>(
            "SELECT balance, tx_count FROM account_summary WHERE account_id = @Id",
            new { Id = streamId });

        summary.balance.Should().Be(1175m); // 1000 + 250 - 75
        summary.tx_count.Should().Be(3);
    }

    /// <summary>
    /// Demonstrates the real power of read models: SQL queries that would be
    /// impossible or very expensive against an event store.
    ///
    /// "Find all accounts with balance > 1000" requires scanning every stream
    /// and replaying events in an event store. With a read model, it's a simple
    /// indexed WHERE clause.
    ///
    /// This test creates 3 accounts with different balances and queries for
    /// high-balance accounts — a trivial SQL query against the read model.
    /// </summary>
    [Fact]
    public async Task ReadModel_ShouldBeQueryableWithSql()
    {
        var repo = CreateRepo();
        var projector = CreateProjector();

        // Create 3 accounts with different balances
        var testId = Guid.NewGuid().ToString("N")[..6];
        var accounts = new[]
        {
            (id: $"account-rich-{testId}", name: $"Rich-{testId}", deposit: 5000m),
            (id: $"account-mid-{testId}", name: $"Mid-{testId}", deposit: 500m),
            (id: $"account-poor-{testId}", name: $"Poor-{testId}", deposit: 50m),
        };

        foreach (var acct in accounts)
        {
            var opened = new AccountOpened
            {
                AccountId = acct.id,
                OwnerName = acct.name,
                InitialDeposit = acct.deposit
            };

            using (var session = await repo.BeginSessionFor(acct.id))
            {
                session.AddEvent(opened);
                await session.SaveChanges();
            }

            await projector.ProjectAccountOpened(opened);
        }

        // SQL query: find accounts with balance > 1000
        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        var highBalanceAccounts = (await connection.QueryAsync<AccountSummaryRow>(
            "SELECT account_id, owner_name, balance FROM account_summary WHERE balance > 1000 AND account_id LIKE @Pattern",
            new { Pattern = $"%-{testId}" })).ToList();

        highBalanceAccounts.Should().HaveCount(1);
        highBalanceAccounts[0].owner_name.Should().Be($"Rich-{testId}");
        highBalanceAccounts[0].balance.Should().Be(5000m);
    }

    /// <summary>
    /// Demonstrates that a single event stream can project into MULTIPLE read model tables.
    ///
    /// The AccountOpened and MoneyDeposited events project into both:
    ///   1. account_summary — current state (balance, owner, tx count)
    ///   2. transaction_history — append-only log of all transactions
    ///
    /// This is a core CQRS pattern: one source of truth (events), many read models
    /// optimized for different query patterns:
    ///   - "What is my current balance?" → account_summary
    ///   - "Show my last 5 transactions" → transaction_history
    ///   - "What is the total deposited this month?" → could be a third read model
    /// </summary>
    [Fact]
    public async Task ReadModel_ShouldSupportMultipleTables()
    {
        var repo = CreateRepo();
        var projector = CreateProjector();
        var streamId = UniqueStreamId();

        var openedEvent = new AccountOpened
        {
            AccountId = streamId,
            OwnerName = "Carol",
            InitialDeposit = 200m
        };
        var depositEvent = new MoneyDeposited { Amount = 300m, Description = "Freelance payment" };
        var withdrawEvent = new MoneyWithdrawn { Amount = 50m, Description = "Coffee subscription" };

        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(openedEvent);
            session.AddEvent(depositEvent);
            session.AddEvent(withdrawEvent);
            await session.SaveChanges();
        }

        await projector.ProjectAccountOpened(openedEvent);
        await projector.ProjectMoneyDeposited(streamId, depositEvent);
        await projector.ProjectMoneyWithdrawn(streamId, withdrawEvent);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();

        // Table 1: account_summary — current state snapshot
        var summary = await connection.QuerySingleAsync<AccountSummaryRow>(
            "SELECT balance, tx_count FROM account_summary WHERE account_id = @Id",
            new { Id = streamId });

        summary.balance.Should().Be(450m); // 200 + 300 - 50
        summary.tx_count.Should().Be(3);

        // Table 2: transaction_history — full transaction log
        var transactions = (await connection.QueryAsync<TransactionHistoryRow>(
            "SELECT event_type, amount, description FROM transaction_history WHERE account_id = @Id ORDER BY id",
            new { Id = streamId })).ToList();

        transactions.Should().HaveCount(3);
        transactions[0].event_type.Should().Be("AccountOpened");
        transactions[0].amount.Should().Be(200m);
        transactions[1].event_type.Should().Be("MoneyDeposited");
        transactions[1].amount.Should().Be(300m);
        transactions[1].description.Should().Be("Freelance payment");
        transactions[2].event_type.Should().Be("MoneyWithdrawn");
        transactions[2].amount.Should().Be(50m);
        transactions[2].description.Should().Be("Coffee subscription");
    }

    /// <summary>
    /// Demonstrates the "rebuild from scratch" pattern — one of the most powerful
    /// properties of event sourcing.
    ///
    /// Because the event store is the source of truth, you can:
    ///   1. Delete all read model tables
    ///   2. Replay every event from the event store
    ///   3. Rebuild the read model from scratch
    ///
    /// This is invaluable for:
    ///   - Fixing bugs in projection logic (fix the code, rebuild, done)
    ///   - Adding new read models (backfill from existing events)
    ///   - Disaster recovery (read model database corruption)
    ///   - Schema migrations (drop old table, create new one, replay)
    ///
    /// The test proves that after destroying and rebuilding the read model,
    /// the result is identical to the original — because events are immutable.
    /// </summary>
    [Fact]
    public async Task ReadModel_ShouldHandleReplayFromScratch()
    {
        var repo = CreateRepo();
        var projector = CreateProjector();
        var streamId = UniqueStreamId();

        var openedEvent = new AccountOpened
        {
            AccountId = streamId,
            OwnerName = "Dave",
            InitialDeposit = 100m
        };
        var deposit1 = new MoneyDeposited { Amount = 50m, Description = "Gift" };
        var deposit2 = new MoneyDeposited { Amount = 75m, Description = "Refund" };

        // Write events to the event store (these survive the read model reset)
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(openedEvent);
            session.AddEvent(deposit1);
            session.AddEvent(deposit2);
            await session.SaveChanges();
        }

        // Project initially
        await projector.ProjectAccountOpened(openedEvent);
        await projector.ProjectMoneyDeposited(streamId, deposit1);
        await projector.ProjectMoneyDeposited(streamId, deposit2);

        // Verify initial projection
        await using var conn1 = await _fixture.DataSource.OpenConnectionAsync();
        var beforeReset = await conn1.QuerySingleAsync<AccountSummaryRow>(
            "SELECT balance FROM account_summary WHERE account_id = @Id",
            new { Id = streamId });
        beforeReset.balance.Should().Be(225m);

        // DESTROY the read model tables — simulates corruption or migration
        await ReadModelSchema.ResetSchemaAsync(_fixture.DataSource);

        // Verify read model is empty
        await using var conn2 = await _fixture.DataSource.OpenConnectionAsync();
        var afterReset = await conn2.QuerySingleOrDefaultAsync<AccountSummaryRow>(
            "SELECT balance FROM account_summary WHERE account_id = @Id",
            new { Id = streamId });
        afterReset.Should().BeNull("read model was destroyed");

        // REBUILD: replay events from the event store through BullOak
        // Load the event-sourced state (this reads events from the events table)
        using (var session = await repo.BeginSessionFor(streamId))
        {
            // State was successfully rehydrated from the event store — events survive!
            var state = session.GetCurrentState();
            state.Balance.Should().Be(225m, "event store still has all events");
        }

        // Re-project the events into the fresh read model
        await projector.ProjectAccountOpened(openedEvent);
        await projector.ProjectMoneyDeposited(streamId, deposit1);
        await projector.ProjectMoneyDeposited(streamId, deposit2);

        // Verify the rebuilt read model is identical to the original
        await using var conn3 = await _fixture.DataSource.OpenConnectionAsync();
        var rebuilt = await conn3.QuerySingleAsync<AccountSummaryRow>(
            "SELECT balance, tx_count FROM account_summary WHERE account_id = @Id",
            new { Id = streamId });

        rebuilt.balance.Should().Be(225m, "rebuilt read model matches original");
        rebuilt.tx_count.Should().Be(3);
    }

    /// <summary>
    /// Demonstrates that events stored through BullOak's session can also be
    /// read back and verified against the read model — ensuring consistency
    /// between the write side (event store) and the read side (read model).
    ///
    /// This test appends events across multiple sessions (simulating operations
    /// over time), projects each batch, and verifies both sides agree.
    /// </summary>
    [Fact]
    public async Task EventStoreAndReadModel_ShouldBeConsistent()
    {
        var repo = CreateRepo();
        var projector = CreateProjector();
        var streamId = UniqueStreamId();

        // Session 1: open account
        var openedEvent = new AccountOpened
        {
            AccountId = streamId,
            OwnerName = "Eve",
            InitialDeposit = 1000m
        };

        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(openedEvent);
            await session.SaveChanges();
        }

        await projector.ProjectAccountOpened(openedEvent);

        // Session 2: deposit (across sessions, simulating time gap)
        var deposit = new MoneyDeposited { Amount = 500m, Description = "Bonus" };
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(deposit);
            await session.SaveChanges();
        }

        await projector.ProjectMoneyDeposited(streamId, deposit);

        // Session 3: withdrawal
        var withdrawal = new MoneyWithdrawn { Amount = 200m, Description = "Rent" };
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(withdrawal);
            await session.SaveChanges();
        }

        await projector.ProjectMoneyWithdrawn(streamId, withdrawal);

        // Verify WRITE SIDE (event store → BullOak rehydration)
        using (var session = await repo.BeginSessionFor(streamId))
        {
            var state = session.GetCurrentState();
            state.Balance.Should().Be(1300m); // 1000 + 500 - 200
            state.TransactionCount.Should().Be(3);
        }

        // Verify READ SIDE (read model)
        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        var summary = await connection.QuerySingleAsync<AccountSummaryRow>(
            "SELECT balance, tx_count FROM account_summary WHERE account_id = @Id",
            new { Id = streamId });

        // Both sides must agree
        summary.balance.Should().Be(1300m);
        summary.tx_count.Should().Be(3);
    }

    #region Dapper row types

    /// <summary>
    /// Dapper DTO for the account_summary table.
    /// Column names match PostgreSQL (snake_case) exactly.
    /// </summary>
    private class AccountSummaryRow
    {
        public string account_id { get; set; } = "";
        public string owner_name { get; set; } = "";
        public decimal balance { get; set; }
        public int tx_count { get; set; }
    }

    /// <summary>
    /// Dapper DTO for the transaction_history table.
    /// </summary>
    private class TransactionHistoryRow
    {
        public string account_id { get; set; } = "";
        public string event_type { get; set; } = "";
        public decimal amount { get; set; }
        public string description { get; set; } = "";
    }

    #endregion
}
