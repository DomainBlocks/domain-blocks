using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClient2Tests : EventStoreClientTests
{
    protected override Task<ITestEventStoreClientFactory<object>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());

        var clientFactory = new TestMongoEventStoreClient2Factory<object>(
            TestMongoConnectionStrings.Default,
            "domainblocks_tests_v2",
            eventTypeMap);

        return Task.FromResult<ITestEventStoreClientFactory<object>>(clientFactory);
    }
}