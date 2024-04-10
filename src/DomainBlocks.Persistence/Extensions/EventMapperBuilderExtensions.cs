using System.Text.Json;
using DomainBlocks.Persistence.Builders;
using Serialization_JsonSerializer = DomainBlocks.Persistence.Serialization.JsonSerializer;

namespace DomainBlocks.Persistence.Extensions;

public static class EventMapperBuilderExtensions
{
    public static EventMapperBuilder UseJsonSerialization(
        this EventMapperBuilder builder, JsonSerializerOptions? options = null)
    {
        builder.SetSerializer(new Serialization_JsonSerializer(options));
        return builder;
    }
}