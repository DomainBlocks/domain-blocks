using System.Text.Json;
using DomainBlocks.Core.Serialization;
using SqlStreamStore;

namespace DomainBlocks.SqlStreamStore;

public class SqlStreamStoreOptionsBuilder
{
    public SqlStreamStoreOptions Options { get; private set; } = new();

    public SqlStreamStoreOptionsBuilder UseJsonSerialization(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        Options = Options
            .WithEventDataSerializerFactory(() => new JsonStringEventDataSerializer(jsonSerializerOptions));

        return this;
    }

    internal void WithStreamStoreFactory(Func<IStreamStore> factory)
    {
        Options = Options.WithStreamStoreFactory(factory);
    }
}