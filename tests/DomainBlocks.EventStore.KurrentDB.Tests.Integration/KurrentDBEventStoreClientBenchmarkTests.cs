using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDBEventStoreClientBenchmarkTests : EventStoreClientBenchmarkTests
{
    protected override Task<ITestEventStoreClientFactory<object>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());

        var clientFactory = new TestKurrentDBEventStoreClientFactory<object>(
            TestConnectionStrings.Default,
            eventTypeMap);

        return Task.FromResult<ITestEventStoreClientFactory<object>>(clientFactory);
    }
}