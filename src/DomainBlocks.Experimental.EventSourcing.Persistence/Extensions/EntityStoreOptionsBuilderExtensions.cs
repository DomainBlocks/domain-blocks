using System.Text.Json;
using DomainBlocks.Core.Serialization;
using DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

public static class EntityStoreOptionsBuilderExtensions
{
    public static EntityStoreOptionsBuilder<ReadOnlyMemory<byte>> UseJsonSerialization(
        this EntityStoreOptionsBuilder<ReadOnlyMemory<byte>> builder,
        JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonBytesEventDataSerializer(options));
        return builder;
    }

    public static EntityStoreOptionsBuilder<string> UseJsonSerialization(
        this EntityStoreOptionsBuilder<string> builder,
        JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonStringEventDataSerializer(options));
        return builder;
    }
}