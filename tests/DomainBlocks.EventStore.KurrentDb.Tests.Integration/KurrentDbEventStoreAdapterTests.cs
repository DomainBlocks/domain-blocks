using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.EventStore.KurrentDB;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using NUnit.Framework;


namespace DomainBlocks.EventStore.KurrentDb.Tests.Integration;

[TestFixture]
public class KurrentDbEventStoreAdapterTests : EventStoreAdapterTests<ReadOnlyMemory<byte>>
{
    protected override Task<IEventStoreAdapter<ReadOnlyMemory<byte>>> CreateEventStoreAdapterAsync()
    {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";
        var client = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));
        IEventStoreAdapter<ReadOnlyMemory<byte>> adapter = new KurrentDbEventStoreAdapter(client);
        return Task.FromResult(adapter);
    }

    protected override UncommittedEvent<ReadOnlyMemory<byte>> CreateTestEvent(string eventName)
    {
        return TestEventsHelper.CreateTestEvent(eventName);
    }
}