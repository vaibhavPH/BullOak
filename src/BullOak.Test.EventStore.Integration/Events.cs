namespace BullOak.Test.EventStore.Integration;

/// <summary>
/// Simple domain events used in tests.
/// In event sourcing, events are plain data objects that describe what happened.
/// </summary>
public record OrderCreated(
    Guid OrderId,
    string CustomerName,
    DateTime CreatedAt);

public record ItemAddedToOrder(
    Guid OrderId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

public record OrderCompleted(
    Guid OrderId,
    DateTime CompletedAt);
