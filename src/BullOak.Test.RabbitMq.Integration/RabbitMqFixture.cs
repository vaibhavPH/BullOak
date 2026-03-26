using Testcontainers.RabbitMq;

namespace BullOak.Test.RabbitMq.Integration;

/// <summary>
/// Shared fixture that spins up a RabbitMQ container for all tests in the collection.
///
/// RabbitMQ is a message broker that implements the AMQP protocol. In event sourcing,
/// it serves as the transport layer for publishing events to external consumers.
///
/// The management plugin is included in the image for debugging — you can inspect
/// queues, exchanges, and messages via the management UI (port 15672).
///
/// Container configuration:
///   - Image: rabbitmq:3-management-alpine (lightweight with management UI)
///   - Default credentials: guest/guest (fine for testing)
///   - Testcontainers handles port mapping and lifecycle automatically
/// </summary>
public class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container;

    public string ConnectionString { get; private set; } = null!;
    public string Hostname { get; private set; } = null!;
    public int AmqpPort { get; private set; }

    public RabbitMqFixture()
    {
        // RabbitMqBuilder includes a built-in health check that waits for the
        // AMQP port (5672) to accept connections before StartAsync completes.
        _container = new RabbitMqBuilder("rabbitmq:3-management-alpine")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        Hostname = _container.Hostname;
        AmqpPort = _container.GetMappedPublicPort(5672);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
