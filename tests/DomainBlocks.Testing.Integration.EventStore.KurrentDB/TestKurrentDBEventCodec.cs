using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.Testing.Integration.EventStore.KurrentDB;

public static class TestKurrentDBEventCodec
{
    public static IEventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> Create<TEvent>(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<TEvent>>? contractMappers = null)
        where TEvent : notnull
    {
        eventFormat ??= EventFormat.Json;

        if (eventFormat != EventFormat.Json)
            throw new NotSupportedException($"{eventFormat} is not supported by the KurrentDB test codec.");

        var codecOptions = new EventCodecOptions<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = eventTypeMap,
            EventSerializer = new JsonUtf8BytesObjectSerializer(),
            MetadataSerializer = new JsonUtf8BytesMetadataSerializer(),
            ContractMappers = contractMappers ?? []
        };

        return EventCodec.Create(codecOptions);
    }
}