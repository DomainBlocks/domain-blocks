using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDBEventStoreBenchmarkTests : EventStoreBenchmarkTests<StreamPosition, Position>
{
    protected override Task<ITestEventStoreFactory<object, string, StreamPosition, Position>> GetEventStoreFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());
        var factory = new TestKurrentDBEventStoreFactory<object>(TestConnectionStrings.Default, eventTypeMap);
        return Task.FromResult<ITestEventStoreFactory<object, string, StreamPosition, Position>>(factory);
    }

    protected override StreamPosition CreateStreamPosition(ulong value) => StreamPosition.FromStreamRevision(value);
}