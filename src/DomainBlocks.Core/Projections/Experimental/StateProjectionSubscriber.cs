using DomainBlocks.Core.Serialization;
using DomainBlocks.Core.Subscriptions;

namespace DomainBlocks.Core.Projections.Experimental;

internal sealed class StateProjectionSubscriber<TRawEvent, TPosition, TState> :
    IEventStreamSubscriber<TRawEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    where TState : class
{
    private readonly StateProjectionOptions<TRawEvent, TPosition, TState> _options;
    private readonly IReadEventAdapter<TRawEvent> _readEventAdapter;
    private SubscriptionStatus _subscriptionStatus;
    private IDisposable? _resource;
    private TState? _state;
    private object? _currentDeserializedEvent;
    private IReadOnlyDictionary<string, string>? _currentDeserializedMetadata;

    public StateProjectionSubscriber(
        StateProjectionOptions<TRawEvent, TPosition, TState> options,
        IReadEventAdapter<TRawEvent> readEventAdapter)
    {
        _options = options;
        _readEventAdapter = readEventAdapter;
    }

    public CheckpointFrequency CatchUpCheckpointFrequency => _options.CatchupCheckpointFrequency;

    public CheckpointFrequency LiveCheckpointFrequency => _options.LiveCheckpointFrequency;

    public async Task<TPosition?> OnStarting(CancellationToken cancellationToken)
    {
        _subscriptionStatus = SubscriptionStatus.Starting;
        var state = GetOrCreateState();
        return await _options.OnStartingCallback(state, cancellationToken);
    }

    public async Task OnCatchingUp(CancellationToken cancellationToken)
    {
        _subscriptionStatus = SubscriptionStatus.CatchingUp;
        var state = GetOrCreateState();
        await _options.OnCatchingUpCallback(state, cancellationToken);
    }

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

        // We create the state before deserializing in case of a deserialize exception. We want to give any error
        // handler the opportunity to update state as appropriate in this case.
        var state = GetOrCreateState();

        _currentDeserializedEvent = await _readEventAdapter.DeserializeEvent(@event, eventType, cancellationToken);
        _currentDeserializedMetadata = _readEventAdapter.DeserializeMetadata(@event);

        await onEvent(_currentDeserializedEvent, _currentDeserializedMetadata, state, cancellationToken);

        _currentDeserializedEvent = null;
        _currentDeserializedMetadata = null;

        return OnEventResult.Processed;
    }

    public async Task OnCheckpoint(TPosition position, CancellationToken cancellationToken)
    {
        // If we have no state instance, then there is nothing to checkpoint. This can happen when using a composite
        // subscriber, where one of the other subscribers processes an event that this subscriber didn't care about.
        if (_state == null)
        {
            return;
        }

        await _options.OnCheckpointCallback(_state, position, cancellationToken);
        CleanUpStateIfRequired();
    }

    public async Task OnLive(CancellationToken cancellationToken)
    {
        _subscriptionStatus = SubscriptionStatus.Live;

        if (_state == null)
        {
            return;
        }

        await _options.OnLiveCallback(_state, cancellationToken);

        // Clean the state if required here. This allows us to free resources in the case there were no events during
        // catchup.
        CleanUpStateIfRequired();
    }

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

            return _options.OnEventErrorCallback(eventError, _state!, cancellationToken);
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
        // TODO (DS): What should we do with the state here?
        return _options.OnSubscriptionDroppedCallback(reason, exception, cancellationToken);
    }

    private TState GetOrCreateState()
    {
        if (_state != null)
        {
            return _state;
        }

        _resource = _options.ResourceFactory();
        _state = _options.StateFactory(_resource, _subscriptionStatus);
        return _state;
    }

    private void CleanUpStateIfRequired()
    {
        if (_options.StateLifetime == ProjectionStateLifetime.Singleton)
        {
            return;
        }

        (_state as IDisposable)?.Dispose();
        _state = null;

        _resource!.Dispose();
        _resource = null;
    }
}