using System.Text.Json;
using DomainBlocks.Core.Serialization;
using DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

public static class EntityStreamConfigBuilderExtensions
{
    public static EntityStreamConfigBuilder<ReadOnlyMemory<byte>> UseJsonSerialization(
        this EntityStreamConfigBuilder<ReadOnlyMemory<byte>> builder, JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonBytesEventDataSerializer(options));
        return builder;
    }

    public static EntityStreamConfigBuilder<string> UseJsonSerialization(
        this EntityStreamConfigBuilder<string> builder, JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonStringEventDataSerializer(options));
        return builder;
    }
}