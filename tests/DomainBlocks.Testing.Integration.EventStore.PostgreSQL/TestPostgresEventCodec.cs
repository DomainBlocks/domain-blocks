using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.PostgreSQL;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

public static class TestPostgresEventCodec
{
    public static IEventCodec<TEvent, PostgresEventData, string> Create<TEvent>(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<TEvent>>? contractMappers = null)
        where TEvent : notnull
    {
        eventFormat ??= EventFormat.Json;

        var eventSerializer = eventFormat switch
        {
            EventFormat.Json => new JsonObjectSerializer().AsPostgresEventDataSerializer(),
            EventFormat.Protobuf => ((IObjectSerializer<byte[]>)new ProtobufBytesObjectSerializer()).AsPostgresEventDataSerializer(),
            EventFormat.Bson => throw new NotSupportedException("BSON is not supported by the PostgreSQL event store."),
            _ => throw new ArgumentOutOfRangeException(nameof(eventFormat), eventFormat, null)
        };

        var codecOptions = new EventCodecOptions<TEvent, PostgresEventData, string>
        {
            TypeMap = eventTypeMap,
            EventSerializer = eventSerializer,
            MetadataSerializer = new JsonMetadataSerializer(),
            ContractMappers = contractMappers ?? []
        };

        return EventCodec.Create(codecOptions);
    }
}