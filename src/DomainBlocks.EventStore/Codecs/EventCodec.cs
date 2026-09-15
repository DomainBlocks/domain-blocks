using DomainBlocks.EventStore.Abstractions.Codecs;

namespace DomainBlocks.EventStore.Codecs;

public static class EventCodec
{
    public static EventCodec<TEvent, TEventData, TMetadata> Create<TEvent, TEventData, TMetadata>(
        EventCodecOptions<TEvent, TEventData, TMetadata> options)
        where TEvent : notnull
        where TEventData : notnull
    {
        var encoderOptions = new EventEncoderOptions<TEvent, TEventData, TMetadata>
        {
            TypeMap = options.TypeMap,
            EventSerializer = options.EventSerializer,
            MetadataSerializer = options.MetadataSerializer,
            ContractMappers = options.ContractMappers
        };

        var decoderOptions = new EventDecoderOptions<TEvent, TEventData, TMetadata>
        {
            TypeMap = options.TypeMap,
            EventDeserializer = options.EventSerializer,
            MetadataDeserializer = options.MetadataSerializer,
            ContractMappers = options.ContractMappers
        };

        return new EventCodec<TEvent, TEventData, TMetadata>
        {
            Encoder = EventEncoder.Create(encoderOptions),
            Decoder = EventDecoder.Create(decoderOptions)
        };
    }
}