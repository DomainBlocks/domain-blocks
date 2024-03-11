using System.Text.Json;
using DomainBlocks.Core.Serialization;
using DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

public static class EntityStreamOptionsBuilderExtensions
{
    public static EntityStreamOptionsBuilder<ReadOnlyMemory<byte>> UseJsonSerialization(
        this EntityStreamOptionsBuilder<ReadOnlyMemory<byte>> builder, JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonBytesEventDataSerializer(options));
        return builder;
    }

    public static EntityStreamOptionsBuilder<string> UseJsonSerialization(
        this EntityStreamOptionsBuilder<string> builder, JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonStringEventDataSerializer(options));
        return builder;
    }
}