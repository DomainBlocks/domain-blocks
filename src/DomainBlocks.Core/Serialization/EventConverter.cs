using DomainBlocks.Core.Metadata;

namespace DomainBlocks.Core.Serialization;

public static class EventConverter
{
    public static EventConverter<TReadEvent, TWriteEvent> Create<TReadEvent, TWriteEvent>(
        IEventNameMap eventNameMap,
        IEventAdapter<TReadEvent, TWriteEvent> eventAdapter,
        EventMetadataContext? metadataContext = null)
    {
        return new EventConverter<TReadEvent, TWriteEvent>(eventNameMap, eventAdapter, metadataContext);
    }
}

public class EventConverter<TReadEvent, TWriteEvent> : IEventConverter<TReadEvent, TWriteEvent>
{
    private readonly IEventNameMap _eventNameMap;
    private readonly IEventAdapter<TReadEvent, TWriteEvent> _eventAdapter;
    private readonly EventMetadataContext _metadataContext;

    public EventConverter(
        IEventNameMap eventNameMap,
        IEventAdapter<TReadEvent, TWriteEvent> eventAdapter,
        EventMetadataContext? metadataContext = null)
    {
        _eventNameMap = eventNameMap ?? throw new ArgumentNullException(nameof(eventNameMap));
        _eventAdapter = eventAdapter ?? throw new ArgumentNullException(nameof(eventAdapter));
        _metadataContext = metadataContext ?? EventMetadataContext.CreateEmpty();
    }

    public ValueTask<object> DeserializeEvent(
        TReadEvent readEvent,
        Type? eventTypeOverride = null,
        CancellationToken cancellationToken = default)
    {
        var eventName = _eventAdapter.GetEventName(readEvent);
        var eventType = eventTypeOverride ?? _eventNameMap.GetEventType(eventName);
        return _eventAdapter.DeserializeEvent(readEvent, eventType, cancellationToken);
    }

    public IReadOnlyDictionary<string, string> DeserializeMetadata(TReadEvent readEvent) =>
        _eventAdapter.DeserializeMetadata(readEvent);

    public TWriteEvent SerializeToWriteEvent(
        object @event,
        string? eventNameOverride = null,
        params KeyValuePair<string, string>[] additionalMetadata)
    {
        var eventName = eventNameOverride ?? _eventNameMap.GetEventName(@event.GetType());
        var metadata = _metadataContext.BuildMetadata(additionalMetadata);
        return _eventAdapter.SerializeToWriteEvent(@event, eventName, metadata);
    }
}