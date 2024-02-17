using System.Text.Json;
using DomainBlocks.Core.Serialization;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

public static class StateEventStreamBindingBuilderExtensions
{
    public static StateEventStreamBinding<TState, ReadOnlyMemory<byte>>.Builder UseJsonSerialization<TState>(
        this StateEventStreamBinding<TState, ReadOnlyMemory<byte>>.Builder builder,
        JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonBytesEventDataSerializer(options));
        return builder;
    }

    public static StateEventStreamBinding<TState, string>.Builder UseJsonSerialization<TState>(
        this StateEventStreamBinding<TState, string>.Builder builder,
        JsonSerializerOptions? options = null)
    {
        builder.SetEventDataSerializer(new JsonStringEventDataSerializer(options));
        return builder;
    }
}