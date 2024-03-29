using EventStore.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.Testing;

[SetUpFixture]
public abstract class EventStoreIntegrationTest : IDisposable
{
    protected internal EventStoreClient EventStoreClient { get; private set; } = null!;

    protected internal EventStorePersistentSubscriptionsClient
        PersistentSubscriptionsClient { get; private set; } = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        var settings = EventStoreClientSettings.Create("esdb://localhost:2114?tls=false");
        EventStoreClient = new EventStoreClient(settings);
        PersistentSubscriptionsClient = new EventStorePersistentSubscriptionsClient(settings);
    }

    public void Dispose()
    {
        EventStoreClient?.Dispose();
    }
}