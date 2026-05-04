using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using MongoDB.Bson;

namespace DomainBlocks.Testing.Integration.MongoDB;

public static class TestMongoEventCodec
{
    public static EventCodec<TEvent, BsonValue, BsonValue> Create<TEvent>(EventTypeMap eventTypeMap)
        where TEvent : notnull
    {
        var encoderOptions = new EventEncoderOptions<TEvent, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap.Appends,
            EventSerializer = new BsonDocumentObjectSerde(),
            MetadataSerializer = new BsonDocumentMetadataSerde()
        };

        var decoderOptions = new EventDecoderOptions<TEvent, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap.Reads,
            EventDeserializer = new BsonDocumentObjectSerde(),
            MetadataDeserializer = new BsonDocumentMetadataSerde()
        };

        return new EventCodec<TEvent, BsonValue, BsonValue>
        {
            Encoder = EventEncoder.Create(encoderOptions),
            Decoder = EventDecoder.Create(decoderOptions)
        };
    }
}