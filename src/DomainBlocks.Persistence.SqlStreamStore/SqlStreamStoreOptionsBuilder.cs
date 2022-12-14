using System;
using System.Text.Json;
using DomainBlocks.Serialization.Json;
using SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore;

public class SqlStreamStoreOptionsBuilder
{
    public SqlStreamStoreOptions Options { get; private set; } = new();

    public SqlStreamStoreOptionsBuilder UseJsonSerialization(JsonSerializerOptions jsonSerializerOptions = null)
    {
        Options = Options.WithEventSerializerFactory(
            eventNameMap => new JsonStringEventSerializer(eventNameMap, jsonSerializerOptions));

        return this;
    }

    internal void WithStreamStoreFactory(Func<IStreamStore> factory)
    {
        Options = Options.WithStreamStoreFactory(factory);
    }
}