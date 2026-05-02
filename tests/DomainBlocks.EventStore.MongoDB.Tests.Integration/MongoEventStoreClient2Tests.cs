using DomainBlocks.Testing.Integration;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClient2Tests : EventStoreClientTests
{
    protected override Task<ITestEventStoreClientFactory<object>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new MongoEventStoreClientOptions2 { DatabaseName = "domainblocks_tests_v2" };
        var clientFactory = TestMongoEventStoreClient2Factory.CreateDefault(options);
        return Task.FromResult<ITestEventStoreClientFactory<object>>(clientFactory);
    }
}