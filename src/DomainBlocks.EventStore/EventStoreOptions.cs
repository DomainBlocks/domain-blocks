using DomainBlocks.Core.Serialization;
using EventStore.Client;

namespace DomainBlocks.EventStore;

public class EventStoreOptions
{
    private static readonly Func<IEventDataSerializer<ReadOnlyMemory<byte>>> DefaultEventDataSerializerFactory =
        () => new JsonBytesEventDataSerializer();

    private Lazy<EventStoreClient>? _eventStoreClient;

    private Func<IEventDataSerializer<ReadOnlyMemory<byte>>>
        _eventSerializerFactory = DefaultEventDataSerializerFactory;

    public EventStoreOptions()
    {
    }

    private EventStoreOptions(EventStoreOptions copyFrom)
    {
        _eventStoreClient = copyFrom._eventStoreClient;
        _eventSerializerFactory = copyFrom._eventSerializerFactory;
    }

    public EventStoreOptions WithEventStoreClientFactory(Func<EventStoreClient> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        return new EventStoreOptions(this) { _eventStoreClient = new Lazy<EventStoreClient>(factory) };
    }

    public EventStoreOptions WithEventDataSerializerFactory(Func<IEventDataSerializer<ReadOnlyMemory<byte>>> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        return new EventStoreOptions(this) { _eventSerializerFactory = factory };
    }

    public EventStoreClient GetOrCreateEventStoreClient()
    {
        if (_eventStoreClient == null)
        {
            throw new InvalidOperationException("Cannot EventStore client as no factory has been specified.");
        }

        return _eventStoreClient.Value;
    }

    public IEventDataSerializer<ReadOnlyMemory<byte>> GetEventDataSerializer() => _eventSerializerFactory();
}