using DomainBlocks.Core.Subscriptions;

namespace DomainBlocks.Core.Projections.Experimental;

internal sealed class StateProjectionOptions<TRawEvent, TPosition, TState>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    where TState : class
{
    private readonly Dictionary<Type, OnEventCallback<object, TState>> _onEventCallbacks = new();

    public StateProjectionOptions()
    {
        ResourceFactory = () => EmptyDisposable.Instance;
        StateFactory = (_, _) => throw new InvalidOperationException("No state factory specified");
        CatchupCheckpointFrequency = CheckpointFrequency.Default;
        LiveCheckpointFrequency = CheckpointFrequency.Default;
        OnStartingCallback = (_, _) => Task.FromResult<TPosition?>(null);
        OnCatchingUpCallback = (_, _) => Task.CompletedTask;
        OnCheckpointCallback = (_, _, _) => Task.CompletedTask;
        OnLiveCallback = (_, _) => Task.CompletedTask;
        OnEventErrorCallback = (_, _, _) => Task.FromResult(EventErrorResolution.Abort);
        OnSubscriptionDroppedCallback = (_, _, _) => Task.CompletedTask;
    }

    private StateProjectionOptions(StateProjectionOptions<TRawEvent, TPosition, TState> copyFrom)
    {
        _onEventCallbacks = copyFrom._onEventCallbacks.ToDictionary(x => x.Key, x => x.Value);
        ResourceFactory = copyFrom.ResourceFactory;
        StateFactory = copyFrom.StateFactory;
        StateLifetime = copyFrom.StateLifetime;
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

    public Func<IDisposable> ResourceFactory { get; private init; }
    public Func<IDisposable, SubscriptionStatus, TState> StateFactory { get; private init; }
    public ProjectionStateLifetime StateLifetime { get; private init; }
    public CheckpointFrequency CatchupCheckpointFrequency { get; private init; }
    public CheckpointFrequency LiveCheckpointFrequency { get; private init; }
    public Func<TState, CancellationToken, Task<TPosition?>> OnStartingCallback { get; private init; }
    public Func<TState, CancellationToken, Task> OnCatchingUpCallback { get; private init; }
    public Func<TState, TPosition, CancellationToken, Task> OnCheckpointCallback { get; private init; }
    public Func<TState, CancellationToken, Task> OnLiveCallback { get; private init; }
    public OnEventErrorCallback<TRawEvent, TPosition, TState> OnEventErrorCallback { get; private init; }

    public Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> OnSubscriptionDroppedCallback
    {
        get;
        private init;
    }

    public IReadOnlyDictionary<Type, OnEventCallback<object, TState>> OnEventCallbacks => _onEventCallbacks;
    public ProjectionEventTypeMap EventTypeMap { get; private init; } = new();

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithSingletonState(TState state)
    {
        return WithStateFactory(() => EmptyDisposable.Instance, (_, _) => state)
            .WithStateLifetime(ProjectionStateLifetime.Singleton);
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithStateFactory(Func<TState> stateFactory)
    {
        return WithStateFactory(() => EmptyDisposable.Instance, (_, _) => stateFactory());
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithStateFactory(
        Func<SubscriptionStatus, TState> stateFactory)
    {
        return WithStateFactory(() => EmptyDisposable.Instance, (_, s) => stateFactory(s));
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithStateFactory<TResource>(
        Func<TResource> resourceFactory,
        Func<TResource, SubscriptionStatus, TState> stateFactory)
        where TResource : IDisposable
    {
        if (resourceFactory == null) throw new ArgumentNullException(nameof(resourceFactory));
        if (stateFactory == null) throw new ArgumentNullException(nameof(stateFactory));

        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this)
        {
            ResourceFactory = () => resourceFactory(),
            StateFactory = (d, s) => stateFactory((TResource)d, s)
        };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithStateLifetime(ProjectionStateLifetime lifetime)
    {
        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this) { StateLifetime = lifetime };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithCatchupCheckpointFrequency(
        CheckpointFrequency checkpointFrequency)
    {
        if (checkpointFrequency == null) throw new ArgumentNullException(nameof(checkpointFrequency));

        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this)
        {
            CatchupCheckpointFrequency = checkpointFrequency
        };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithLiveCheckpointFrequency(
        CheckpointFrequency checkpointFrequency)
    {
        if (checkpointFrequency == null) throw new ArgumentNullException(nameof(checkpointFrequency));

        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this)
        {
            LiveCheckpointFrequency = checkpointFrequency
        };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnStarting(
        Func<TState, CancellationToken, Task<TPosition?>> onStarting)
    {
        if (onStarting == null) throw new ArgumentNullException(nameof(onStarting));
        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this) { OnStartingCallback = onStarting };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnCatchingUp(
        Func<TState, CancellationToken, Task> onCatchingUp)
    {
        if (onCatchingUp == null) throw new ArgumentNullException(nameof(onCatchingUp));
        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this) { OnCatchingUpCallback = onCatchingUp };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnCheckpoint(
        Func<TState, TPosition, CancellationToken, Task> onCheckpoint)
    {
        if (onCheckpoint == null) throw new ArgumentNullException(nameof(onCheckpoint));
        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this) { OnCheckpointCallback = onCheckpoint };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnLive(Func<TState, CancellationToken, Task> onLive)
    {
        if (onLive == null) throw new ArgumentNullException(nameof(onLive));
        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this) { OnLiveCallback = onLive };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnEventError(
        OnEventErrorCallback<TRawEvent, TPosition, TState> onEventError)
    {
        if (onEventError == null) throw new ArgumentNullException(nameof(onEventError));
        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this) { OnEventErrorCallback = onEventError };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnSubscriptionDropped(
        Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> onSubscriptionDropped)
    {
        if (onSubscriptionDropped == null) throw new ArgumentNullException(nameof(onSubscriptionDropped));

        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this)
        {
            OnSubscriptionDroppedCallback = onSubscriptionDropped
        };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> MergeEventTypes(ProjectionEventTypeMap eventTypeMap)
    {
        return new StateProjectionOptions<TRawEvent, TPosition, TState>(this)
        {
            EventTypeMap = EventTypeMap.Merge(eventTypeMap)
        };
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> MapMissingEventTypesWithDefaultNames()
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

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnEvent<TEvent>(Action<TEvent, TState> onEvent)
    {
        return WithOnEvent<TEvent>((e, _, s, _) =>
        {
            onEvent(e, s);
            return Task.CompletedTask;
        });
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnEvent<TEvent>(
        Action<TEvent, IReadOnlyDictionary<string, string>, TState> onEvent)
    {
        return WithOnEvent<TEvent>((e, m, s, _) =>
        {
            onEvent(e, m, s);
            return Task.CompletedTask;
        });
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnEvent<TEvent>(
        Func<TEvent, TState, CancellationToken, Task> onEvent)
    {
        return WithOnEvent<TEvent>((e, _, s, ct) => onEvent(e, s, ct));
    }

    public StateProjectionOptions<TRawEvent, TPosition, TState> WithOnEvent<TEvent>(
        OnEventCallback<TEvent, TState> onEvent)
    {
        if (onEvent == null) throw new ArgumentNullException(nameof(onEvent));

        var eventType = typeof(TEvent);

        var copy = new StateProjectionOptions<TRawEvent, TPosition, TState>(this)
        {
            _onEventCallbacks =
            {
                [eventType] = (e, m, s, ct) => onEvent((TEvent)e, m, s, ct)
            }
        };

        return copy;
    }
}