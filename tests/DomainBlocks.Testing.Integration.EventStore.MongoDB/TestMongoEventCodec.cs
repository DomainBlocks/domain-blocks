using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using MongoDB.Bson;

namespace DomainBlocks.Testing.Integration.EventStore.MongoDB;

public static class TestMongoEventCodec
{
    public static IEventCodec<TEvent, BsonValue, BsonValue> Create<TEvent>(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<TEvent>>? contractMappers = null)
        where TEvent : notnull
    {
        eventFormat ??= EventFormat.Bson;

        var eventSerializer = eventFormat switch
        {
            EventFormat.Bson => new BsonDocumentObjectSerializer(),
            EventFormat.Protobuf => new ProtobufBytesObjectSerializer().AsBsonValueSerializer(),
            EventFormat.Json => new JsonUtf8BytesObjectSerializer().AsBsonValueSerializer(),
            _ => throw new ArgumentOutOfRangeException(nameof(eventFormat), eventFormat, null)
        };

        var codecOptions = new EventCodecOptions<TEvent, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap,
            EventSerializer = eventSerializer,
            MetadataSerializer = new BsonDocumentMetadataSerializer(),
            ContractMappers = contractMappers ?? []
        };

        return EventCodec.Create(codecOptions);
    }
}