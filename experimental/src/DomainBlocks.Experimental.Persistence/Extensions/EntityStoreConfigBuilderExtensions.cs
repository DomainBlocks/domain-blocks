using System.Text.Json;
using DomainBlocks.Experimental.Persistence.Configuration;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class EntityStoreConfigBuilderExtensions
{
    public static EntityStoreConfigBuilder UseJsonSerialization(
        this EntityStoreConfigBuilder builder, JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonEventDataSerializer(options));
        return builder;
    }
}