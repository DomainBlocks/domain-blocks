using System.Text.Json;
using DomainBlocks.Experimental.Persistence.Configuration;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class EntityStreamConfigBuilderExtensions
{
    public static EntityStreamConfigBuilder UseJsonSerialization(
        this EntityStreamConfigBuilder builder, JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonEventDataSerializer(options));
        return builder;
    }
}