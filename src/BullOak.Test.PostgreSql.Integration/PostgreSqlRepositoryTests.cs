using System.Reflection;
using BullOak.Repositories;
using BullOak.Repositories.Exceptions;
using BullOak.Repositories.PostgreSql;
using FluentAssertions;

namespace BullOak.Test.PostgreSql.Integration;

[Collection("PostgreSql")]
public class PostgreSqlRepositoryTests
{
    private readonly PostgreSqlFixture _fixture;
    private readonly IHoldAllConfiguration _configuration;

    public PostgreSqlRepositoryTests(PostgreSqlFixture fixture)
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

    private string UniqueStreamId() => $"account-{Guid.NewGuid()}";

    [Fact]
    public async Task NewStream_IsNewState_ShouldBeTrue()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        using var session = await repo.BeginSessionFor(streamId);

        session.IsNewState.Should().BeTrue();
    }

    [Fact]
    public async Task WriteAndReadSingleEvent_ShouldRehydrateState()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        // Write
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Alice",
                InitialDeposit = 100m
            });
            await session.SaveChanges();
        }

        // Read
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.IsNewState.Should().BeFalse();

            var state = session.GetCurrentState();
            state.AccountId.Should().Be(streamId);
            state.OwnerName.Should().Be("Alice");
            state.Balance.Should().Be(100m);
            state.TransactionCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task WriteMultipleEvents_ShouldRehydrateInOrder()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Bob",
                InitialDeposit = 500m
            });
            session.AddEvent(new MoneyDeposited { Amount = 200m, Description = "Salary" });
            session.AddEvent(new MoneyWithdrawn { Amount = 50m, Description = "Coffee" });
            await session.SaveChanges();
        }

        using (var session = await repo.BeginSessionFor(streamId))
        {
            var state = session.GetCurrentState();
            state.Balance.Should().Be(650m); // 500 + 200 - 50
            state.TransactionCount.Should().Be(3);
        }
    }

    [Fact]
    public async Task AppendToExistingStream_ShouldWork()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        // First session: create
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Carol",
                InitialDeposit = 100m
            });
            await session.SaveChanges();
        }

        // Second session: append
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new MoneyDeposited { Amount = 50m, Description = "Gift" });
            await session.SaveChanges();
        }

        // Third session: verify
        using (var session = await repo.BeginSessionFor(streamId))
        {
            var state = session.GetCurrentState();
            state.Balance.Should().Be(150m);
            state.TransactionCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task ConcurrencyConflict_ShouldThrowConcurrencyException()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        // Create the stream
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Dave",
                InitialDeposit = 100m
            });
            await session.SaveChanges();
        }

        // Load two sessions concurrently
        using var sessionA = await repo.BeginSessionFor(streamId);
        using var sessionB = await repo.BeginSessionFor(streamId);

        // Session A saves first
        sessionA.AddEvent(new MoneyDeposited { Amount = 10m, Description = "From A" });
        await sessionA.SaveChanges();

        // Session B tries to save — should fail with ConcurrencyException
        sessionB.AddEvent(new MoneyDeposited { Amount = 20m, Description = "From B" });

        var act = () => sessionB.SaveChanges();
        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task ThrowIfNotExists_WithEmptyStream_ShouldThrow()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        var act = () => repo.BeginSessionFor(streamId, throwIfNotExists: true);
        await act.Should().ThrowAsync<StreamNotFoundException>();
    }

    [Fact]
    public async Task ThrowIfNotExists_WithExistingStream_ShouldNotThrow()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Eve",
                InitialDeposit = 100m
            });
            await session.SaveChanges();
        }

        var act = () => repo.BeginSessionFor(streamId, throwIfNotExists: true);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Contains_WithNoStream_ShouldReturnFalse()
    {
        var repo = CreateRepo();
        var result = await repo.Contains(UniqueStreamId());
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Contains_WithExistingStream_ShouldReturnTrue()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Frank",
                InitialDeposit = 100m
            });
            await session.SaveChanges();
        }

        var result = await repo.Contains(streamId);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_ShouldRemoveAllEvents()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Grace",
                InitialDeposit = 100m
            });
            await session.SaveChanges();
        }

        await repo.Delete(streamId);

        var exists = await repo.Contains(streamId);
        exists.Should().BeFalse();

        // Loading after delete should give a new/empty state
        using var session2 = await repo.BeginSessionFor(streamId);
        session2.IsNewState.Should().BeTrue();
    }

    [Fact]
    public async Task AppliesAt_ShouldFilterEventsByTimestamp()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        // Write first event
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Heidi",
                InitialDeposit = 100m
            });
            await session.SaveChanges();
        }

        // Record time between events
        var cutoffTime = DateTime.UtcNow;
        await Task.Delay(100); // ensure timestamp separation

        // Write second event
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new MoneyDeposited { Amount = 50m, Description = "Later deposit" });
            await session.SaveChanges();
        }

        // Load with point-in-time — should only see the first event
        using var historicalSession = await repo.BeginSessionFor(streamId, appliesAt: cutoffTime);
        var state = historicalSession.GetCurrentState();
        state.Balance.Should().Be(100m);
        state.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesCalledTwice_ShouldAppendCorrectly()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Ivan",
                InitialDeposit = 100m
            });
            await session.SaveChanges();

            // Add more events in the same session
            session.AddEvent(new MoneyDeposited { Amount = 50m, Description = "Bonus" });
            await session.SaveChanges();
        }

        // Verify all events are persisted
        using (var session = await repo.BeginSessionFor(streamId))
        {
            var state = session.GetCurrentState();
            state.Balance.Should().Be(150m);
            state.TransactionCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task DisposeWithoutSave_ShouldNotPersistEvents()
    {
        var repo = CreateRepo();
        var streamId = UniqueStreamId();

        // Create the stream first
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Judy",
                InitialDeposit = 100m
            });
            await session.SaveChanges();
        }

        // Open session, add event, dispose WITHOUT saving
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new MoneyDeposited { Amount = 999m, Description = "Unsaved" });
            // No SaveChanges — dispose discards
        }

        // Verify the unsaved event was not persisted
        using (var session = await repo.BeginSessionFor(streamId))
        {
            var state = session.GetCurrentState();
            state.Balance.Should().Be(100m); // only the initial deposit
        }
    }
}
