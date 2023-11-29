using DomainBlocks.Core.Subscriptions;

namespace DomainBlocks.Core.Projections;

// TODO (DS): There is some duplication here with StateProjectionOptions. Consider modelling the commonality.
internal sealed class EventHandlerProjectionOptions<TRawEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly Dictionary<Type, OnEventCallback<object>> _onEventCallbacks = new();

    public EventHandlerProjectionOptions()
    {
        CatchupCheckpointFrequency = CheckpointFrequency.Default;
        LiveCheckpointFrequency = CheckpointFrequency.Default;
        OnStartingCallback = _ => Task.FromResult<TPosition?>(null);
        OnCatchingUpCallback = _ => Task.CompletedTask;
        OnCheckpointCallback = (_, _) => Task.CompletedTask;
        OnLiveCallback = _ => Task.CompletedTask;
        OnEventErrorCallback = (_, _) => Task.FromResult(EventErrorResolution.Abort);
        OnSubscriptionDroppedCallback = (_, _, _) => Task.CompletedTask;
    }

    private EventHandlerProjectionOptions(EventHandlerProjectionOptions<TRawEvent, TPosition> copyFrom)
    {
        _onEventCallbacks = copyFrom._onEventCallbacks.ToDictionary(x => x.Key, x => x.Value);
        CatchupCheckpointFrequency = copyFrom.CatchupCheckpointFrequency;
        LiveCheckpointFrequency = copyFrom.LiveCheckpointFrequency;
        OnStartingCallback = copyFrom.OnStartingCallback;
        OnCatchingUpCallback = copyFrom.OnCatchingUpCallback;
        OnCheckpointCallback = copyFrom.OnCheckpointCallback;
        OnLiveCallback = copyFrom.OnLiveCallback;
        OnEventErrorCallback = copyFrom.OnEventErrorCallback;
        OnSubscriptionDroppedCallback = copyFrom.OnSubscriptionDroppedCallback;
        EventTypeMap = copyFrom.EventTypeMap;
    }

    public CheckpointFrequency CatchupCheckpointFrequency { get; private init; }
    public CheckpointFrequency LiveCheckpointFrequency { get; private init; }
    public Func<CancellationToken, Task<TPosition?>> OnStartingCallback { get; private init; }
    public Func<CancellationToken, Task> OnCatchingUpCallback { get; private init; }
    public Func<TPosition, CancellationToken, Task> OnCheckpointCallback { get; private init; }
    public Func<CancellationToken, Task> OnLiveCallback { get; private init; }
    public OnEventErrorCallback<TRawEvent, TPosition> OnEventErrorCallback { get; private init; }

    public Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> OnSubscriptionDroppedCallback
    {
        get;
        private init;
    }

    public IReadOnlyDictionary<Type, OnEventCallback<object>> OnEventCallbacks => _onEventCallbacks;
    public ProjectionEventTypeMap EventTypeMap { get; private init; } = new();

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithCatchupCheckpointFrequency(
        CheckpointFrequency checkpointFrequency)
    {
        if (checkpointFrequency == null) throw new ArgumentNullException(nameof(checkpointFrequency));

        return new EventHandlerProjectionOptions<TRawEvent, TPosition>(this)
        {
            CatchupCheckpointFrequency = checkpointFrequency
        };
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithLiveCheckpointFrequency(
        CheckpointFrequency checkpointFrequency)
    {
        if (checkpointFrequency == null) throw new ArgumentNullException(nameof(checkpointFrequency));

        return new EventHandlerProjectionOptions<TRawEvent, TPosition>(this)
        {
            LiveCheckpointFrequency = checkpointFrequency
        };
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnStarting(
        Func<CancellationToken, Task<TPosition?>> onStarting)
    {
        if (onStarting == null) throw new ArgumentNullException(nameof(onStarting));
        return new EventHandlerProjectionOptions<TRawEvent, TPosition>(this) { OnStartingCallback = onStarting };
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnCatchingUp(
        Func<CancellationToken, Task> onCatchingUp)
    {
        if (onCatchingUp == null) throw new ArgumentNullException(nameof(onCatchingUp));
        return new EventHandlerProjectionOptions<TRawEvent, TPosition>(this) { OnCatchingUpCallback = onCatchingUp };
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnCheckpoint(
        Func<TPosition, CancellationToken, Task> onCheckpoint)
    {
        if (onCheckpoint == null) throw new ArgumentNullException(nameof(onCheckpoint));
        return new EventHandlerProjectionOptions<TRawEvent, TPosition>(this) { OnCheckpointCallback = onCheckpoint };
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnLive(Func<CancellationToken, Task> onLive)
    {
        if (onLive == null) throw new ArgumentNullException(nameof(onLive));
        return new EventHandlerProjectionOptions<TRawEvent, TPosition>(this) { OnLiveCallback = onLive };
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnEventError(
        OnEventErrorCallback<TRawEvent, TPosition> onEventError)
    {
        if (onEventError == null) throw new ArgumentNullException(nameof(onEventError));
        return new EventHandlerProjectionOptions<TRawEvent, TPosition>(this) { OnEventErrorCallback = onEventError };
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnSubscriptionDropped(
        Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> onSubscriptionDropped)
    {
        if (onSubscriptionDropped == null) throw new ArgumentNullException(nameof(onSubscriptionDropped));

        return new EventHandlerProjectionOptions<TRawEvent, TPosition>(this)
        {
            OnSubscriptionDroppedCallback = onSubscriptionDropped
        };
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> MergeEventTypes(ProjectionEventTypeMap eventTypeMap)
    {
        return new EventHandlerProjectionOptions<TRawEvent, TPosition>(this)
        {
            EventTypeMap = EventTypeMap.Merge(eventTypeMap)
        };
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> MapMissingEventTypesWithDefaultNames()
    {
        var eventTypeMap = EventTypeMap;

        foreach (var (eventType, _) in OnEventCallbacks)
        {
            if (!eventTypeMap.IsMapped(eventType))
            {
                eventTypeMap = eventTypeMap.Add(eventType, eventType.Name);
            }
        }

        return MergeEventTypes(eventTypeMap);
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnEvent<TEvent>(Action<TEvent> onEvent)
    {
        return WithOnEvent<TEvent>((e, _, _) =>
        {
            onEvent(e);
            return Task.CompletedTask;
        });
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnEvent<TEvent>(
        Action<TEvent, IReadOnlyDictionary<string, string>> onEvent)
    {
        return WithOnEvent<TEvent>((e, m, _) =>
        {
            onEvent(e, m);
            return Task.CompletedTask;
        });
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnEvent<TEvent>(
        Func<TEvent, CancellationToken, Task> onEvent)
    {
        return WithOnEvent<TEvent>((e, _, ct) => onEvent(e, ct));
    }

    public EventHandlerProjectionOptions<TRawEvent, TPosition> WithOnEvent<TEvent>(OnEventCallback<TEvent> onEvent)
    {
        if (onEvent == null) throw new ArgumentNullException(nameof(onEvent));

        var eventType = typeof(TEvent);

        var copy = new EventHandlerProjectionOptions<TRawEvent, TPosition>(this)
        {
            _onEventCallbacks =
            {
                [eventType] = (e, m, ct) => onEvent((TEvent)e, m, ct)
            }
        };

        return copy;
    }
}