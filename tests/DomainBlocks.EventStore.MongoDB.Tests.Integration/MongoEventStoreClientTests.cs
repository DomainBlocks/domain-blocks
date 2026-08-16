using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClientTests : EventStoreClientTests
{
    protected override Task<ITestEventStoreClientFactory<object>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new MongoEventStoreClientOptions { DatabaseName = $"dbx_test_{Guid.NewGuid():N}" };
        var clientFactory = TestMongoEventStoreClientFactory.CreateDefault(options);
        return Task.FromResult<ITestEventStoreClientFactory<object>>(clientFactory);
    }

    [Test]
    public void Test()
    {
        var definition = ReadDefinition
            .Forward<LogPosition>()
            .FromStart()
            .ThenLive(new LiveConsumerOptions());
    }
}