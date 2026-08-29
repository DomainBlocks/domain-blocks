using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDBEventStoreTests : EventStoreTests<StreamPosition, Position>
{
    protected override Task<ITestEventStoreFactory<object, string, StreamPosition, Position>> GetEventStoreFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());
        var factory = new TestKurrentDBEventStoreFactory<object>(TestConnectionStrings.Default, eventTypeMap);
        return Task.FromResult<ITestEventStoreFactory<object, string, StreamPosition, Position>>(factory);
    }

    protected override StreamPosition CreateStreamPosition(ulong value) => StreamPosition.FromStreamRevision(value);
}