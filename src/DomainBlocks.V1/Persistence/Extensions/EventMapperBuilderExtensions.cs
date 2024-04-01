using System.Text.Json;
using DomainBlocks.V1.Persistence.Builders;
using JsonSerializer = DomainBlocks.V1.Serialization.JsonSerializer;

namespace DomainBlocks.V1.Persistence.Extensions;

public static class EventMapperBuilderExtensions
{
    public static EventMapperBuilder UseJsonSerialization(
        this EventMapperBuilder builder, JsonSerializerOptions? options = null)
    {
        builder.SetSerializer(new JsonSerializer(options));
        return builder;
    }
}