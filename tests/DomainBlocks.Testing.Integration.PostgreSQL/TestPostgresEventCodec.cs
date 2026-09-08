using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.PostgreSQL;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.Testing.Integration.PostgreSQL;

public static class TestPostgresEventCodec
{
    public static EventCodec<TEvent, PostgresEventData, string> Create<TEvent>(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<TEvent>>? contractMappers = null)
        where TEvent : notnull
    {
        eventFormat ??= EventFormat.Json;

        var eventSerde = eventFormat switch
        {
            EventFormat.Json => new JsonObjectSerde().AsPostgresEventDataSerde(),
            EventFormat.Protobuf => ((IObjectSerde<byte[]>)new ProtobufBytesObjectSerde()).AsPostgresEventDataSerde(),
            EventFormat.Bson => throw new NotSupportedException("BSON is not supported by the PostgreSQL event store."),
            _ => throw new ArgumentOutOfRangeException(nameof(eventFormat), eventFormat, null)
        };

        var codecOptions = new EventCodecOptions<TEvent, PostgresEventData, string>
        {
            TypeMap = eventTypeMap,
            EventSerde = eventSerde,
            MetadataSerde = new JsonMetadataSerde(),
            ContractMappers = contractMappers ?? []
        };

        return EventCodec.Create(codecOptions);
    }
}
