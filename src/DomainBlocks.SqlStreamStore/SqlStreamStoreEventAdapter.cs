using System.Collections.ObjectModel;
using SqlStreamStore.Streams;

// ReSharper disable once CheckNamespace
namespace DomainBlocks.Core.Serialization.SqlStreamStore;

public sealed class SqlStreamStoreEventAdapter : IEventAdapter<StreamMessage, NewStreamMessage>
{
    private readonly IEventDataSerializer<string> _serializer;

    public SqlStreamStoreEventAdapter(IEventDataSerializer<string> serializer)
    {
        _serializer = serializer;
    }

    public string GetEventName(StreamMessage readEvent) => readEvent.Type;

    public async ValueTask<object> DeserializeEvent(
        StreamMessage readEvent,
        Type eventType,
        CancellationToken cancellationToken = default)
    {
        var eventData = await readEvent.GetJsonData(cancellationToken);
        return _serializer.Deserialize(eventData, eventType);
    }

    public IReadOnlyDictionary<string, string> DeserializeMetadata(StreamMessage readEvent)
    {
        var json = readEvent.JsonMetadata;
        if (json == null)
        {
            return new Dictionary<string, string>();
        }

        return (IReadOnlyDictionary<string, string>)_serializer.Deserialize(
            json,
            typeof(IReadOnlyDictionary<string, string>));
    }

    public NewStreamMessage SerializeToWriteEvent(
        object @event,
        string? eventName,
        IEnumerable<KeyValuePair<string, string>> metadata)
    {
        var serializedEvent = _serializer.Serialize(@event);
        var serializedMetadata = _serializer.Serialize(metadata);
        return new NewStreamMessage(Guid.NewGuid(), eventName, serializedEvent, serializedMetadata);
    }
}