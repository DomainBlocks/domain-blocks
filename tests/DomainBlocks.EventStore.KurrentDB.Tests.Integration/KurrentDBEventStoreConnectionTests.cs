using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDBEventStoreConnectionTests : EventStoreClientTests
{
    protected override Task<IEventStoreClient<IDomainEvent>> CreateClientAsync()
    {
        var codecOptions = new EventCodecOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = new EventTypeMapBuilder().MapType<TestEvent>().Build(),
            EventSerializer = new SystemTextJsonBytesSerializer(),
            MetadataSerializer = new SystemTextJsonBytesMetadataSerializer()
        };

        var codec = EventCodec.Create(codecOptions);

        const string connectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";
        var kurrentClient = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));

        return Task.FromResult<IEventStoreClient<IDomainEvent>>(
            new KurrentDBEventStoreClient<IDomainEvent>(kurrentClient, codec));
    }
}