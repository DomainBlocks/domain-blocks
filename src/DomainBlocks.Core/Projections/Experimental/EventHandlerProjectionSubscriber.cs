using DomainBlocks.Core.Serialization;
using DomainBlocks.Core.Subscriptions;

namespace DomainBlocks.Core.Projections.Experimental;

internal sealed class EventHandlerProjectionSubscriber<TRawEvent, TPosition> :
    IEventStreamSubscriber<TRawEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly EventHandlerProjectionOptions<TRawEvent, TPosition> _options;
    private readonly IReadEventAdapter<TRawEvent> _readEventAdapter;
    private object? _currentDeserializedEvent;
    private IReadOnlyDictionary<string, string>? _currentDeserializedMetadata;

    public EventHandlerProjectionSubscriber(
        EventHandlerProjectionOptions<TRawEvent, TPosition> options,
        IReadEventAdapter<TRawEvent> readEventAdapter)
    {
        _options = options;
        _readEventAdapter = readEventAdapter;
    }

    public CheckpointFrequency CatchUpCheckpointFrequency => _options.CatchupCheckpointFrequency;

    public CheckpointFrequency LiveCheckpointFrequency => _options.LiveCheckpointFrequency;

    public Task<TPosition?> OnStarting(CancellationToken cancellationToken) => _options.OnStarting(cancellationToken);

    public Task OnCatchingUp(CancellationToken cancellationToken) => _options.OnCatchingUp(cancellationToken);

    public async Task<OnEventResult> OnEvent(TRawEvent @event, TPosition position, CancellationToken cancellationToken)
    {
        var eventName = _readEventAdapter.GetEventName(@event);

        // Check if we have a mapped type for this event.
        // TODO (DS): Is it a valid use case to deserialize an event to multiple CLR types?
        var eventType = _options.EventTypeMap.GetClrTypes(eventName).FirstOrDefault();
        if (eventType == null)
        {
            return OnEventResult.Ignored;
        }

        // Check if we have a handler.
        if (!_options.OnEventCallbacks.TryGetValue(eventType, out var onEvent))
        {
            return OnEventResult.Ignored;
        }

        _currentDeserializedEvent = await _readEventAdapter.DeserializeEvent(@event, eventType, cancellationToken);
        _currentDeserializedMetadata = _readEventAdapter.DeserializeMetadata(@event);

        await onEvent(_currentDeserializedEvent, _currentDeserializedMetadata, cancellationToken);

        _currentDeserializedEvent = null;
        _currentDeserializedMetadata = null;

        return OnEventResult.Processed;
    }

    public Task OnCheckpoint(TPosition position, CancellationToken cancellationToken) =>
        _options.OnCheckpoint(position, cancellationToken);

    public Task OnLive(CancellationToken cancellationToken) => _options.OnLive(cancellationToken);

    public Task<EventErrorResolution> OnEventError(
        TRawEvent @event,
        TPosition position,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventError = EventError.Create(
                @event,
                position,
                _currentDeserializedEvent!,
                _currentDeserializedMetadata!,
                exception);

            return _options.OnEventError(eventError, cancellationToken);
        }
        finally
        {
            _currentDeserializedEvent = null;
            _currentDeserializedMetadata = null;
        }
    }

    public Task OnSubscriptionDropped(
        SubscriptionDroppedReason reason,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        return _options.OnSubscriptionDropped(reason, exception, cancellationToken);
    }
}