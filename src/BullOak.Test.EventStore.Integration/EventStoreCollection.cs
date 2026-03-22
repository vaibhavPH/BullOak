namespace BullOak.Test.EventStore.Integration;

/// <summary>
/// xUnit collection definition that shares one EventStoreFixture across all test classes
/// decorated with [Collection("EventStore")]. This means the Docker container starts once
/// and is reused by every test class in the collection — much faster than per-class containers.
/// </summary>
[CollectionDefinition("EventStore")]
public class EventStoreCollection : ICollectionFixture<EventStoreFixture>
{
}
