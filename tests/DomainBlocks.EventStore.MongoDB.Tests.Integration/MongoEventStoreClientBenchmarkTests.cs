using DomainBlocks.Testing.Integration;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreBenchmarkTests : EventStoreBenchmarkTests
{
    protected override Task<ITestEventStoreFactory<object>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new MongoEventStoreClientOptions { DatabaseName = $"dbx_test_{Guid.NewGuid():N}" };
        var clientFactory = TestMongoEventStoreClientFactory.CreateDefault(options);
        return Task.FromResult<ITestEventStoreFactory<object>>(clientFactory);
    }
}