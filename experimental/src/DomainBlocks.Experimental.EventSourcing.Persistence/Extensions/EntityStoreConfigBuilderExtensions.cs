using System.Text.Json;
using DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;
using DomainBlocks.Experimental.EventSourcing.Persistence.Serialization;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

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