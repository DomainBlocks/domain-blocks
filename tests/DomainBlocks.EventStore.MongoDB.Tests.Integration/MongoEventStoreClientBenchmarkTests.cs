using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreBenchmarkTests : EventStoreBenchmarkTests<StreamPosition, LogPosition>
{
    protected override Task<ITestEventStoreFactory<object, string, StreamPosition, LogPosition>>
        GetEventStoreFactoryAsync(CancellationToken cancellationToken = default)
    {
        var options = new MongoEventStoreOptions { DatabaseName = $"dbx_test_{Guid.NewGuid():N}" };
        var factory = TestMongoEventStoreFactory.CreateDefault(options);
        return Task.FromResult<ITestEventStoreFactory<object, string, StreamPosition, LogPosition>>(factory);
    }

    protected override StreamPosition CreateStreamPosition(ulong value) => new(value);
}