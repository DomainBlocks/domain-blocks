using System.Text.Json;
using DomainBlocks.Core.Serialization;
using EventStore.Client;

namespace DomainBlocks.EventStore;

public class EventStoreOptionsBuilder
{
    public EventStoreOptions Options { get; private set; } = new();

    public EventStoreOptionsBuilder WithInstance(EventStoreClient eventStoreClient)
    {
        WithEventStoreStoreFactory(() => eventStoreClient);
        return this;
    }

    public EventStoreOptionsBuilder WithSettings(EventStoreClientSettings settings)
    {
        WithEventStoreStoreFactory(() => new EventStoreClient(settings));
        return this;
    }

    public EventStoreOptionsBuilder UseJsonSerialization(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        Options = Options
            .WithEventDataSerializerFactory(() => new JsonBytesEventDataSerializer(jsonSerializerOptions));

        return this;
    }

    private void WithEventStoreStoreFactory(Func<EventStoreClient> factory)
    {
        Options = Options.WithEventStoreClientFactory(factory);
    }
}