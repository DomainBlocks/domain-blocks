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
            TypeMap = options.TypeMap.Appends,
            EventSerializer = options.EventSerde,
            MetadataSerializer = options.MetadataSerde,
            MetadataContributors = options.MetadataContributors,
            ContractMappers = options.ContractMappers
        };

        var decoderOptions = new EventDecoderOptions<TEvent, TEventData, TMetadata>
        {
            TypeMap = options.TypeMap.Reads,
            EventDeserializer = options.EventSerde,
            MetadataDeserializer = options.MetadataSerde,
            ContractMappers = options.ContractMappers
        };

        return new EventCodec<TEvent, TEventData, TMetadata>
        {
            Encoder = EventEncoder.Create(encoderOptions),
            Decoder = EventDecoder.Create(decoderOptions)
        };
    }
}