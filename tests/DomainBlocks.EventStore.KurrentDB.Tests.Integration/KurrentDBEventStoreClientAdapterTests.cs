using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDBEventStoreClientAdapterTests : EventStoreClientAdapterTests<ReadOnlyMemory<byte>>
{
    protected override Task<IEventStoreClientAdapter<ReadOnlyMemory<byte>>> CreateEventStoreAdapterAsync()
    {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";
        var client = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));
        IEventStoreClientAdapter<ReadOnlyMemory<byte>> adapter = new KurrentDBEventStoreClientAdapter(client);
        return Task.FromResult(adapter);
    }

    protected override UncommittedEvent<ReadOnlyMemory<byte>> CreateTestEvent(string eventName)
    {
        return TestEventsHelper.CreateTestEvent(eventName);
    }
}