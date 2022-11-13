using System;
using System.Text.Json;
using SqlStreamStore;

namespace DomainBlocks.Projections.SqlStreamStore.New;

public class SqlStreamStoreOptionsBuilder
{
    public SqlStreamStoreOptions Options { get; private set; } = new();

    public SqlStreamStoreOptionsBuilder UseJsonSerialization(JsonSerializerOptions jsonSerializerOptions = null)
    {
        Options = Options.WithEventDeserializerFactory(() => new StreamMessageJsonDeserializer(jsonSerializerOptions));
        return this;
    }
    
    internal void WithStreamStoreFactory(Func<IStreamStore> streamStoreFactory)
    {
        Options = Options.WithStreamStoreFactory(streamStoreFactory);
    }
}