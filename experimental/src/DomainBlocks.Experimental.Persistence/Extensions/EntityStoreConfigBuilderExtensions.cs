using System.Text.Json;
using DomainBlocks.Experimental.Persistence.Configuration;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class EntityStoreConfigBuilderExtensions
{
    public static EntityStoreConfigBuilder<ReadOnlyMemory<byte>> UseJsonSerialization(
        this EntityStoreConfigBuilder<ReadOnlyMemory<byte>> builder,
        JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonBytesEventDataSerializer(options));
        return builder;
    }

    public static EntityStoreConfigBuilder<string> UseJsonSerialization(
        this EntityStoreConfigBuilder<string> builder,
        JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonStringEventDataSerializer(options));
        return builder;
    }
}