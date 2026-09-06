using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using MongoDB.Bson;

namespace DomainBlocks.Testing.Integration.MongoDB;

public static class TestMongoEventCodec
{
    public static EventCodec<TEvent, BsonValue, BsonValue> Create<TEvent>(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<TEvent>>? contractMappers = null)
        where TEvent : notnull
    {
        eventFormat ??= EventFormat.Bson;

        var eventSerde = eventFormat switch
        {
            EventFormat.Bson => new BsonDocumentObjectSerde(),
            EventFormat.Protobuf => new ProtobufBytesObjectSerde().AsBsonValueSerde(),
            EventFormat.Json => new JsonUtf8BytesObjectSerde().AsBsonValueSerde(),
            _ => throw new ArgumentOutOfRangeException(nameof(eventFormat), eventFormat, null)
        };

        var codecOptions = new EventCodecOptions<TEvent, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap,
            EventSerde = eventSerde,
            MetadataSerde = new BsonDocumentMetadataSerde(),
            ContractMappers = contractMappers ?? []
        };

        return EventCodec.Create(codecOptions);
    }
}