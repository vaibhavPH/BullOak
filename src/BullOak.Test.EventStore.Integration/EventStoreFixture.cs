using EventStore.Client;
using Testcontainers.KurrentDb;

namespace BullOak.Test.EventStore.Integration;

/// <summary>
/// Shared fixture that spins up an EventStoreDB (KurrentDB) container once for all tests in the collection.
/// Uses the dedicated Testcontainers.KurrentDb module for type-safe container configuration.
/// Implements IAsyncLifetime so xUnit manages the container lifecycle automatically.
/// </summary>
public class EventStoreFixture : IAsyncLifetime
{
    private readonly KurrentDbContainer _container;

    public EventStoreClient Client { get; private set; } = null!;

    public string ConnectionString { get; private set; } = null!;

    public EventStoreFixture()
    {
        // KurrentDbBuilder provides a dedicated builder for EventStoreDB/KurrentDB.
        // The image is passed to the constructor (parameterless ctor is obsolete in 4.11+).
        _container = new KurrentDbBuilder("eventstore/eventstore:latest")
            .WithPortBinding(KurrentDbBuilder.KurrentDbPort, false) // random host port
            .WithEnvironment("EVENTSTORE_INSECURE", "true")
            .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
            .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "All")
            .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "true")
            .WithEnvironment("EVENTSTORE_MEM_DB", "true") // in-memory, no disk needed
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(KurrentDbBuilder.KurrentDbPort);

        ConnectionString = $"esdb://{host}:{port}?tls=false";

        var settings = EventStoreClientSettings.Create(ConnectionString);
        Client = new EventStoreClient(settings);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }
}
