using System.Text.Json;
using DomainBlocks.Experimental.Persistence.Builders;
using DomainBlocks.Experimental.Persistence.Events;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class EventMapperBuilderExtensions
{
    public static EventMapperBuilder UseJsonSerialization(
        this EventMapperBuilder builder, JsonSerializerOptions? options = null)
    {
        builder.SetSerializer(new JsonEventDataSerializer(options));
        return builder;
    }
}