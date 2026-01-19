using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public static class EventCodec
{
    public static EventCodec<TEvent, TEventData, TMetadata> Create<TEvent, TEventData, TMetadata>(
        EventCodecOptions<TEvent, TEventData, TMetadata> options)
        where TEvent : notnull
        where TEventData : notnull
    {
        var encoderOptions = new EventEncoderOptions<TEvent, TEventData, TMetadata>
        {
            TypeMap = options.TypeMap.Append,
            EventSerializer = options.EventSerde,
            MetadataSerializer = options.MetadataSerde,
            MetadataContributors = options.MetadataContributors,
            ContractMappers = options.ContractMappers
        };

        var decoderOptions = new EventDecoderOptions<TEvent, TEventData, TMetadata>
        {
            TypeMap = options.TypeMap.Read,
            EventDeserializer = options.EventSerde,
            MetadataDeserializer = options.MetadataSerde,
            ContractMappers = options.ContractMappers
        };

        return new EventCodec<TEvent, TEventData, TMetadata>
        {
            Encoder = EventEncoder.CreateFactory(encoderOptions),
            Decoder = EventDecoder.Create(decoderOptions)
        };
    }
}