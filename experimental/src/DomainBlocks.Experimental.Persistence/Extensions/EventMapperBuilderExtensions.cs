using System.Text.Json;
using DomainBlocks.Experimental.Persistence.Builders;
using JsonSerializer = DomainBlocks.Experimental.Persistence.Serialization.JsonSerializer;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class EventMapperBuilderExtensions
{
    public static EventMapperBuilder UseJsonSerialization(
        this EventMapperBuilder builder, JsonSerializerOptions? options = null)
    {
        builder.SetSerializer(new JsonSerializer(options));
        return builder;
    }
}