using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDBEventStoreTests : EventStoreTests<StreamPosition, Position>
{
    protected override Abstractions.IEventStore<object, string, StreamPosition, Position> CreateEventStore(
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

    protected override StreamPosition CreateStreamPosition(ulong value) => StreamPosition.FromStreamRevision(value);
}