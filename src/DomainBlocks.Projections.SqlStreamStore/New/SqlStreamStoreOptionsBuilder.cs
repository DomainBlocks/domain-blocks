using System;
using System.Text.Json;
using SqlStreamStore;

namespace DomainBlocks.Projections.SqlStreamStore.New;

public class SqlStreamStoreOptionsBuilder
{
    public SqlStreamStoreOptions Options { get; private set; } = new();

    public void WithStreamStoreFactory(Func<IStreamStore> streamStoreFactory)
    {
        Options = Options.WithStreamStoreFactory(streamStoreFactory);
    }

    public void UseJsonSerialization(JsonSerializerOptions jsonSerializerOptions = null)
    {
        Options = Options.WithEventDeserializerFactory(() => new StreamMessageJsonDeserializer(jsonSerializerOptions));
    }
}