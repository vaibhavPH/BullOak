using BullOak.Repositories;
using BullOak.Repositories.EventPublisher;
using MassTransit;

namespace BullOak.Test.RabbitMq.Integration;

/// <summary>
/// A BullOak event publisher that bridges events to RabbitMQ via MassTransit.
///
/// BullOak's event publishing pipeline works as follows:
///   1. You call session.SaveChanges()
///   2. BullOak persists events to the event store
///   3. For each persisted event, BullOak calls IPublishEvents.Publish()
///   4. This publisher forwards the event to RabbitMQ via MassTransit's IBus
///
/// This enables event-driven architectures: after an event is stored, other services
/// (or other parts of the same service) can react to it asynchronously via RabbitMQ.
///
/// MassTransit handles:
///   - Serialization (JSON by default)
///   - Exchange/queue topology creation
///   - Message routing based on event type
///   - Consumer registration and lifecycle
///   - Retry policies and error handling
/// </summary>
public class MassTransitEventPublisher : IPublishEvents
{
    private readonly IBus _bus;

    public MassTransitEventPublisher(IBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    /// <summary>
    /// Publishes an event to RabbitMQ via MassTransit.
    /// MassTransit creates an exchange named after the event type
    /// and routes the message to all bound queues.
    /// </summary>
    public async Task Publish(ItemWithType @event, CancellationToken cancellationToken)
    {
        // MassTransit.Publish routes based on the runtime type
        await _bus.Publish(@event.instance, @event.type, cancellationToken);
    }

    /// <summary>
    /// Synchronous publish — wraps the async version.
    /// Used when BullOak is configured for synchronous event publishing.
    /// </summary>
    public void PublishSync(ItemWithType @event)
    {
        _bus.Publish(@event.instance, @event.type).GetAwaiter().GetResult();
    }
}
