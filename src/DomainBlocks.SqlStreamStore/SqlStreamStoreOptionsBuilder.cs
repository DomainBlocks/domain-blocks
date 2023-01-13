using System.Text.Json;
using DomainBlocks.Core.Serialization;
using DomainBlocks.ThirdParty.SqlStreamStore;

namespace DomainBlocks.SqlStreamStore;

public class SqlStreamStoreOptionsBuilder
{
    public SqlStreamStoreOptions Options { get; private set; } = new();

    public SqlStreamStoreOptionsBuilder WithInstance(IStreamStore streamStore)
    {
        WithStreamStoreFactory(() => streamStore);
        return this;
    }

    public SqlStreamStoreOptionsBuilder UseJsonSerialization(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        Options = Options
            .WithEventDataSerializerFactory(() => new JsonStringEventDataSerializer(jsonSerializerOptions));

        return this;
    }

    public void WithStreamStoreFactory(Func<IStreamStore> factory)
    {
        Options = Options.WithStreamStoreFactory(factory);
    }
}