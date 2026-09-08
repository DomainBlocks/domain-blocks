using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using NUnit.Framework;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDBEventStoreBenchmarkTests : EventStoreBenchmarkTests<StreamPosition, Position>
{
    protected override IEventStore<object, string, StreamPosition, Position> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        var codecOptions = new EventCodecOptions<object, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = eventTypeMap,
            EventSerde = new JsonUtf8BytesObjectSerde(),
            MetadataSerde = new JsonUtf8BytesMetadataSerde(),
            ContractMappers = contractMappers ?? []
        };

        var eventCodec = EventCodec.Create(codecOptions);

        return new KurrentDBEventStore<object>(SetUpFixture.KurrentDBClient, eventCodec);
    }
}