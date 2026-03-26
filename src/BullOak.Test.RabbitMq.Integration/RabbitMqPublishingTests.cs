using System.Reflection;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace BullOak.Test.RabbitMq.Integration;

/// <summary>
/// Integration tests demonstrating event publishing from BullOak to RabbitMQ via MassTransit.
///
/// In an event-sourced system, events serve two purposes:
///   1. Persistence: events are stored in the event store (source of truth)
///   2. Notification: events are published to a message bus so other components can react
///
/// This "store then publish" pattern enables:
///   - Read model updates (a consumer receives events and updates a SQL database)
///   - Cross-service communication (other microservices react to domain events)
///   - Notifications (send an email when an account is opened)
///   - Analytics (feed events into a data pipeline)
///
/// BullOak supports event publishing through the IPublishEvents interface.
/// When you configure BullOak with .WithEventPublisher(...), every call to
/// SaveChanges() will publish each event through your publisher after persistence.
///
/// MassTransit is the de-facto .NET messaging framework. It provides:
///   - Transport abstraction (RabbitMQ, Azure Service Bus, Amazon SQS, etc.)
///   - Consumer registration and lifecycle management
///   - Message serialization and type-based routing
///   - Built-in retry, circuit breaker, and error handling
///   - An in-memory test harness for unit tests
///
/// These tests use a REAL RabbitMQ container to validate the full flow:
///   BullOak SaveChanges → IPublishEvents → MassTransit → RabbitMQ → Consumer
/// </summary>
[Collection("RabbitMq")]
public class RabbitMqPublishingTests
{
    private readonly RabbitMqFixture _fixture;

    public RabbitMqPublishingTests(RabbitMqFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Demonstrates publishing a single event through MassTransit to RabbitMQ
    /// and receiving it in a consumer.
    ///
    /// Flow:
    ///   1. Configure MassTransit with RabbitMQ transport and a consumer
    ///   2. Publish an AccountOpened event via MassTransit's IBus
    ///   3. Consumer receives the message on a dedicated queue
    ///   4. Verify the consumer processed the event with correct data
    ///
    /// MassTransit creates the following RabbitMQ topology automatically:
    ///   - Exchange: BullOak.Test.RabbitMq.Integration:AccountOpened (fanout)
    ///   - Queue: account-opened-consumer (bound to the exchange)
    ///   - Any published AccountOpened message is routed to all bound queues
    /// </summary>
    [Fact]
    public async Task PublishEvent_ShouldBeReceivedByConsumer()
    {
        // Arrange: set up MassTransit with RabbitMQ transport
        var receivedEvents = new List<AccountOpened>();
        var eventReceived = new TaskCompletionSource<bool>();

        await using var provider = await BuildServiceProviderAsync(cfg =>
        {
            cfg.AddConsumer<TestAccountOpenedConsumer>();

            cfg.UsingRabbitMq((context, rabbitCfg) =>
            {
                rabbitCfg.Host(_fixture.Hostname, (ushort)_fixture.AmqpPort, "/", h =>
                {
                    h.Username("guest");
                    h.Password("guest");
                });

                rabbitCfg.ReceiveEndpoint("account-opened-consumer", e =>
                {
                    e.ConfigureConsumer<TestAccountOpenedConsumer>(context);
                });
            });
        }, services =>
        {
            services.AddSingleton(receivedEvents);
            services.AddSingleton(eventReceived);
        });

        var bus = provider.GetRequiredService<IBus>();

        // Act: publish an event
        var accountId = $"account-{Guid.NewGuid()}";
        await bus.Publish(new AccountOpened
        {
            AccountId = accountId,
            OwnerName = "Alice",
            InitialDeposit = 500m
        });

        // Wait for the consumer to process (with timeout)
        var received = await Task.WhenAny(eventReceived.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        // Assert
        receivedEvents.Should().HaveCount(1);
        receivedEvents[0].AccountId.Should().Be(accountId);
        receivedEvents[0].OwnerName.Should().Be("Alice");
        receivedEvents[0].InitialDeposit.Should().Be(500m);
    }

    /// <summary>
    /// Demonstrates the full BullOak → MassTransit → RabbitMQ → Consumer pipeline.
    ///
    /// This test wires up BullOak's event publishing hook (IPublishEvents) to MassTransit.
    /// When SaveChanges() is called, BullOak:
    ///   1. Persists events to the in-memory event store
    ///   2. Calls IPublishEvents.Publish() for each event
    ///   3. MassTransitEventPublisher forwards each event to RabbitMQ
    ///   4. The consumer receives the event asynchronously
    ///
    /// This is the recommended pattern for integrating BullOak with message buses.
    /// The .WithEventPublisher() configuration hook makes this seamless.
    /// </summary>
    [Fact]
    public async Task BullOakSaveChanges_ShouldPublishEventsToRabbitMq()
    {
        // Arrange: set up MassTransit consumer
        var receivedEvents = new List<object>();
        var allEventsReceived = new TaskCompletionSource<bool>();
        var expectedEventCount = 2;

        await using var provider = await BuildServiceProviderAsync(cfg =>
        {
            cfg.AddConsumer<TestAllEventsConsumer>();

            cfg.UsingRabbitMq((context, rabbitCfg) =>
            {
                rabbitCfg.Host(_fixture.Hostname, (ushort)_fixture.AmqpPort, "/", h =>
                {
                    h.Username("guest");
                    h.Password("guest");
                });

                rabbitCfg.ReceiveEndpoint("bulloak-all-events", e =>
                {
                    e.ConfigureConsumer<TestAllEventsConsumer>(context);
                });
            });
        }, services =>
        {
            services.AddSingleton(receivedEvents);
            services.AddSingleton(allEventsReceived);
            services.AddSingleton(new ExpectedEventCount(expectedEventCount));
        });

        var bus = provider.GetRequiredService<IBus>();

        // Configure BullOak WITH the MassTransit event publisher
        var publisher = new MassTransitEventPublisher(bus);
        var configuration = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithEventPublisher(publisher)   // <-- THIS is the integration point
            .WithAnyAppliersFrom(Assembly.GetExecutingAssembly())
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var repo = new InMemoryEventSourcedRepository<string, AccountState>(configuration);
        var streamId = $"account-{Guid.NewGuid()}";

        // Act: save events through BullOak — this triggers publishing
        using (var session = await repo.BeginSessionFor(streamId))
        {
            session.AddEvent(new AccountOpened
            {
                AccountId = streamId,
                OwnerName = "Bob",
                InitialDeposit = 1000m
            });
            session.AddEvent(new MoneyDeposited
            {
                Amount = 250m,
                Description = "Salary"
            });
            await session.SaveChanges();
        }

        // Wait for consumers to process
        await Task.WhenAny(allEventsReceived.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        // Assert: consumer should have received both events
        receivedEvents.Should().HaveCountGreaterOrEqualTo(2);
        receivedEvents.Should().ContainSingle(e => e is AccountOpened);
        receivedEvents.Should().ContainSingle(e => e is MoneyDeposited);
    }

    /// <summary>
    /// Demonstrates the fan-out pattern: multiple consumers on different queues
    /// all receive the same published event.
    ///
    /// In RabbitMQ, MassTransit creates a fanout exchange per message type.
    /// When you bind multiple queues to the same exchange, every message is
    /// delivered to ALL queues (fan-out). This enables:
    ///   - Read model updater receives the event and updates a database
    ///   - Notification service receives the same event and sends an email
    ///   - Analytics pipeline receives the same event and updates metrics
    ///
    /// Each consumer processes independently — if one fails, others are unaffected.
    /// </summary>
    [Fact]
    public async Task MultipleConsumers_ShouldAllReceiveEvents()
    {
        // Arrange: two independent consumer lists
        var consumer1Events = new List<AccountOpened>();
        var consumer1Done = new TaskCompletionSource<bool>();
        var consumer2Events = new List<AccountOpened>();
        var consumer2Done = new TaskCompletionSource<bool>();

        await using var provider = await BuildServiceProviderAsync(cfg =>
        {
            cfg.AddConsumer<FanoutConsumer1>();
            cfg.AddConsumer<FanoutConsumer2>();

            cfg.UsingRabbitMq((context, rabbitCfg) =>
            {
                rabbitCfg.Host(_fixture.Hostname, (ushort)_fixture.AmqpPort, "/", h =>
                {
                    h.Username("guest");
                    h.Password("guest");
                });

                // Two different queues bound to the same exchange
                rabbitCfg.ReceiveEndpoint("fanout-consumer-1", e =>
                {
                    e.ConfigureConsumer<FanoutConsumer1>(context);
                });

                rabbitCfg.ReceiveEndpoint("fanout-consumer-2", e =>
                {
                    e.ConfigureConsumer<FanoutConsumer2>(context);
                });
            });
        }, services =>
        {
            services.AddKeyedSingleton("consumer1", consumer1Events);
            services.AddKeyedSingleton("consumer1", consumer1Done);
            services.AddKeyedSingleton("consumer2", consumer2Events);
            services.AddKeyedSingleton("consumer2", consumer2Done);
        });

        var bus = provider.GetRequiredService<IBus>();

        // Act: publish one event
        var accountId = $"account-{Guid.NewGuid()}";
        await bus.Publish(new AccountOpened
        {
            AccountId = accountId,
            OwnerName = "Fanout-Test",
            InitialDeposit = 999m
        });

        // Wait for both consumers
        await Task.WhenAll(
            Task.WhenAny(consumer1Done.Task, Task.Delay(TimeSpan.FromSeconds(10))),
            Task.WhenAny(consumer2Done.Task, Task.Delay(TimeSpan.FromSeconds(10))));

        // Assert: both consumers received the same event
        consumer1Events.Should().HaveCount(1);
        consumer1Events[0].AccountId.Should().Be(accountId);

        consumer2Events.Should().HaveCount(1);
        consumer2Events[0].AccountId.Should().Be(accountId);
    }

    /// <summary>
    /// Demonstrates that multiple event types can be published and consumed
    /// by type-specific consumers on separate queues.
    ///
    /// MassTransit creates one exchange per event type. Each consumer listens
    /// to its specific event type. This is type-safe routing — no switch statements,
    /// no string-based routing keys.
    ///
    /// Flow:
    ///   BullOak publishes AccountOpened → Exchange:AccountOpened → Queue:account-events → Consumer
    ///   BullOak publishes MoneyDeposited → Exchange:MoneyDeposited → Queue:deposit-events → Consumer
    /// </summary>
    [Fact]
    public async Task DifferentEventTypes_ShouldRouteToCorrectConsumers()
    {
        var openedEvents = new List<AccountOpened>();
        var openedDone = new TaskCompletionSource<bool>();
        var depositEvents = new List<MoneyDeposited>();
        var depositDone = new TaskCompletionSource<bool>();

        await using var provider = await BuildServiceProviderAsync(cfg =>
        {
            cfg.AddConsumer<TypedAccountOpenedConsumer>();
            cfg.AddConsumer<TypedMoneyDepositedConsumer>();

            cfg.UsingRabbitMq((context, rabbitCfg) =>
            {
                rabbitCfg.Host(_fixture.Hostname, (ushort)_fixture.AmqpPort, "/", h =>
                {
                    h.Username("guest");
                    h.Password("guest");
                });

                rabbitCfg.ReceiveEndpoint("typed-account-events", e =>
                {
                    e.ConfigureConsumer<TypedAccountOpenedConsumer>(context);
                });

                rabbitCfg.ReceiveEndpoint("typed-deposit-events", e =>
                {
                    e.ConfigureConsumer<TypedMoneyDepositedConsumer>(context);
                });
            });
        }, services =>
        {
            services.AddKeyedSingleton("opened", openedEvents);
            services.AddKeyedSingleton("opened", openedDone);
            services.AddKeyedSingleton("deposited", depositEvents);
            services.AddKeyedSingleton("deposited", depositDone);
        });

        var bus = provider.GetRequiredService<IBus>();

        // Publish different event types
        await bus.Publish(new AccountOpened
        {
            AccountId = "typed-test",
            OwnerName = "TypeTest",
            InitialDeposit = 100m
        });

        await bus.Publish(new MoneyDeposited
        {
            Amount = 50m,
            Description = "Type routing test"
        });

        await Task.WhenAll(
            Task.WhenAny(openedDone.Task, Task.Delay(TimeSpan.FromSeconds(10))),
            Task.WhenAny(depositDone.Task, Task.Delay(TimeSpan.FromSeconds(10))));

        // Assert: each consumer received only its event type
        openedEvents.Should().HaveCount(1);
        openedEvents[0].OwnerName.Should().Be("TypeTest");

        depositEvents.Should().HaveCount(1);
        depositEvents[0].Description.Should().Be("Type routing test");
    }

    #region Test Consumers

    /// <summary>
    /// Simple consumer that collects AccountOpened events for assertion.
    /// </summary>
    private class TestAccountOpenedConsumer : IConsumer<AccountOpened>
    {
        private readonly List<AccountOpened> _events;
        private readonly TaskCompletionSource<bool> _done;

        public TestAccountOpenedConsumer(
            List<AccountOpened> events,
            TaskCompletionSource<bool> done)
        {
            _events = events;
            _done = done;
        }

        public Task Consume(ConsumeContext<AccountOpened> context)
        {
            _events.Add(context.Message);
            _done.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Consumer that collects all event types and signals when expected count is reached.
    /// Used by the BullOak integration test.
    /// </summary>
    private class TestAllEventsConsumer :
        IConsumer<AccountOpened>,
        IConsumer<MoneyDeposited>
    {
        private readonly List<object> _events;
        private readonly TaskCompletionSource<bool> _done;
        private readonly int _expectedCount;

        public TestAllEventsConsumer(
            List<object> events,
            TaskCompletionSource<bool> done,
            ExpectedEventCount expectedCount)
        {
            _events = events;
            _done = done;
            _expectedCount = expectedCount.Count;
        }

        public Task Consume(ConsumeContext<AccountOpened> context)
        {
            _events.Add(context.Message);
            if (_events.Count >= _expectedCount) _done.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task Consume(ConsumeContext<MoneyDeposited> context)
        {
            _events.Add(context.Message);
            if (_events.Count >= _expectedCount) _done.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    /// <summary>Fan-out consumer 1 — independent queue.</summary>
    private class FanoutConsumer1 : IConsumer<AccountOpened>
    {
        private readonly List<AccountOpened> _events;
        private readonly TaskCompletionSource<bool> _done;

        public FanoutConsumer1(
            [FromKeyedServices("consumer1")] List<AccountOpened> events,
            [FromKeyedServices("consumer1")] TaskCompletionSource<bool> done)
        {
            _events = events;
            _done = done;
        }

        public Task Consume(ConsumeContext<AccountOpened> context)
        {
            _events.Add(context.Message);
            _done.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    /// <summary>Fan-out consumer 2 — independent queue.</summary>
    private class FanoutConsumer2 : IConsumer<AccountOpened>
    {
        private readonly List<AccountOpened> _events;
        private readonly TaskCompletionSource<bool> _done;

        public FanoutConsumer2(
            [FromKeyedServices("consumer2")] List<AccountOpened> events,
            [FromKeyedServices("consumer2")] TaskCompletionSource<bool> done)
        {
            _events = events;
            _done = done;
        }

        public Task Consume(ConsumeContext<AccountOpened> context)
        {
            _events.Add(context.Message);
            _done.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    /// <summary>Type-specific consumer for AccountOpened only.</summary>
    private class TypedAccountOpenedConsumer : IConsumer<AccountOpened>
    {
        private readonly List<AccountOpened> _events;
        private readonly TaskCompletionSource<bool> _done;

        public TypedAccountOpenedConsumer(
            [FromKeyedServices("opened")] List<AccountOpened> events,
            [FromKeyedServices("opened")] TaskCompletionSource<bool> done)
        {
            _events = events;
            _done = done;
        }

        public Task Consume(ConsumeContext<AccountOpened> context)
        {
            _events.Add(context.Message);
            _done.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    /// <summary>Type-specific consumer for MoneyDeposited only.</summary>
    private class TypedMoneyDepositedConsumer : IConsumer<MoneyDeposited>
    {
        private readonly List<MoneyDeposited> _events;
        private readonly TaskCompletionSource<bool> _done;

        public TypedMoneyDepositedConsumer(
            [FromKeyedServices("deposited")] List<MoneyDeposited> events,
            [FromKeyedServices("deposited")] TaskCompletionSource<bool> done)
        {
            _events = events;
            _done = done;
        }

        public Task Consume(ConsumeContext<MoneyDeposited> context)
        {
            _events.Add(context.Message);
            _done.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Simple wrapper for passing expected event count through DI.
    /// </summary>
    public record ExpectedEventCount(int Count);

    /// <summary>
    /// Wraps a ServiceProvider and implements IAsyncDisposable so it can be used
    /// with "await using". MassTransit registers IAsyncDisposable services (UsageTracker),
    /// which means the ServiceProvider must be disposed asynchronously.
    /// </summary>
    private sealed class AsyncServiceProvider : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public AsyncServiceProvider(ServiceProvider provider) => _provider = provider;

        public T GetRequiredService<T>() where T : notnull
            => _provider.GetRequiredService<T>();

        public async ValueTask DisposeAsync()
        {
            // Stop the bus gracefully before disposing
            var busControl = _provider.GetRequiredService<IBusControl>();
            await busControl.StopAsync();
            await _provider.DisposeAsync();
        }
    }

    /// <summary>
    /// Builds a service provider with MassTransit and RabbitMQ configured.
    /// Starts the bus and waits for consumers to bind before returning.
    /// Returns an IAsyncDisposable wrapper — use "await using" to dispose.
    /// </summary>
    private static async Task<AsyncServiceProvider> BuildServiceProviderAsync(
        Action<IBusRegistrationConfigurator> configureMassTransit,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();

        services.AddMassTransit(configureMassTransit);
        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();

        // Start the bus (connects to RabbitMQ, creates exchanges/queues)
        var busControl = provider.GetRequiredService<IBusControl>();
        await busControl.StartAsync();

        // Give consumers time to bind to queues
        await Task.Delay(1000);

        return new AsyncServiceProvider(provider);
    }

    #endregion
}
