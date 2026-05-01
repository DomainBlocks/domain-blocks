using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClientTests : EventStoreClientTests
{
    protected override Task<ITestEventStoreClientFactory<object>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());

        var clientFactory = new TestMongoEventStoreClientFactory<object>(
            TestMongoConnectionStrings.Default,
            "domainblocks_tests",
            eventTypeMap);

        return Task.FromResult<ITestEventStoreClientFactory<object>>(clientFactory);
    }
}