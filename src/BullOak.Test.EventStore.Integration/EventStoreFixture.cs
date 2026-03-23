using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using EventStore.Client;

namespace BullOak.Test.EventStore.Integration;

/// <summary>
/// Shared fixture that spins up an EventStoreDB container once for all tests in the collection.
/// Implements IAsyncLifetime so xUnit manages the container lifecycle automatically.
/// </summary>
public class EventStoreFixture : IAsyncLifetime
{
    private const int EventStoreHttpPort = 2113;

    private readonly IContainer _container;

    public EventStoreClient Client { get; private set; } = null!;

    public string ConnectionString { get; private set; } = null!;

    public EventStoreFixture()
    {
        // EventStoreDB runs in insecure mode (no TLS) for testing.
        // We expose port 2113 which serves both gRPC and HTTP.
        _container = new ContainerBuilder()
            .WithImage("eventstore/eventstore:latest")
            .WithPortBinding(EventStoreHttpPort, true) // random host port → container 2113
            .WithEnvironment("EVENTSTORE_INSECURE", "true")
            .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
            .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "All")
            .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "true")
            .WithEnvironment("EVENTSTORE_MEM_DB", "true") // in-memory, no disk needed
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r
                        .ForPort(EventStoreHttpPort)
                        .ForPath("/health/live")
                        .ForStatusCode(System.Net.HttpStatusCode.NoContent)))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(EventStoreHttpPort);

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
