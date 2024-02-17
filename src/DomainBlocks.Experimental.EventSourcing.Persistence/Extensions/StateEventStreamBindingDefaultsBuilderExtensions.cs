using System.Text.Json;
using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

public static class StateEventStreamBindingDefaultsBuilderExtensions
{
    public static StateEventStreamBindingDefaults<ReadOnlyMemory<byte>>.Builder UseJsonSerialization(
        this StateEventStreamBindingDefaults<ReadOnlyMemory<byte>>.Builder builder,
        JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonBytesEventDataSerializer(options));
        return builder;
    }

    public static StateEventStreamBindingDefaults<string>.Builder UseJsonSerialization(
        this StateEventStreamBindingDefaults<string>.Builder builder,
        JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonStringEventDataSerializer(options));
        return builder;
    }
}