using DomainBlocks.Experimental.Persistence.Events;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class EventDataSerializerExtensions
{
    public static object Deserialize(this IEventDataSerializer serializer, ReadEvent readEvent, Type type)
    {
        return readEvent.DataType == EventDataType.Bytes
            ? serializer.Deserialize(readEvent.BytesData.Payload.Span, type)
            : serializer.Deserialize(readEvent.StringData.Payload, type);
    }
}