using DomainBlocks.Testing.Integration;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClientBenchmarkTests : EventStoreClientBenchmarkTests
{
    protected override Task<ITestEventStoreClientFactory<object>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new MongoEventStoreNodeOptions { DatabaseName = "domainblocks_benchmark_tests" };
        var clientFactory = TestMongoEventStoreClientFactory.CreateDefault(options);
        return Task.FromResult<ITestEventStoreClientFactory<object>>(clientFactory);
    }
}