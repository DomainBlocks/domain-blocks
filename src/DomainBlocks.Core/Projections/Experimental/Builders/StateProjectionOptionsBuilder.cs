using DomainBlocks.Core.Serialization;
using DomainBlocks.Core.Subscriptions;
using DomainBlocks.Core.Subscriptions.Builders;

namespace DomainBlocks.Core.Projections.Experimental.Builders;

public sealed class StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> :
    IEventStreamConsumerBuilder<TRawEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    where TState : class
{
    private readonly EventStreamConsumerBuilder<TRawEvent, TPosition> _consumerBuilder;
    private StateProjectionOptions<TRawEvent, TPosition, TState> _options;

    internal StateProjectionOptionsBuilder(
        EventStreamConsumerBuilder<TRawEvent, TPosition> consumerBuilder,
        StateProjectionOptions<TRawEvent, TPosition, TState> options)
    {
        _consumerBuilder = consumerBuilder;
        _options = options;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> WithPerCheckpointLifetime()
    {
        _options = _options.WithStateLifetime(ProjectionStateLifetime.PerCheckpoint);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> WithSingletonLifetime()
    {
        _options = _options.WithStateLifetime(ProjectionStateLifetime.Singleton);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> WithCheckpoints(
        Action<CheckpointFrequencyBuilder> builderAction)
    {
        var builder = new CheckpointFrequencyBuilder();
        builderAction(builder);
        var checkpointFrequency = builder.Build();

        _options = _options
            .WithCatchupCheckpointFrequency(checkpointFrequency)
            .WithLiveCheckpointFrequency(checkpointFrequency);

        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> WithCatchUpCheckpoints(
        Action<CheckpointFrequencyBuilder> builderAction)
    {
        var builder = new CheckpointFrequencyBuilder();
        builderAction(builder);
        _options = _options.WithCatchupCheckpointFrequency(builder.Build());
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> WithLiveCheckpoints(
        Action<CheckpointFrequencyBuilder> builderAction)
    {
        var builder = new CheckpointFrequencyBuilder();
        builderAction(builder);
        _options = _options.WithLiveCheckpointFrequency(builder.Build());
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> OnStarting(
        Func<TState, CancellationToken, Task<TPosition?>> onStarting)
    {
        _options = _options.WithOnStarting(onStarting);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> OnCatchingUp(
        Func<TState, CancellationToken, Task> onCatchingUp)
    {
        _options = _options.WithOnCatchingUp(onCatchingUp);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> OnCheckpoint(
        Func<TState, TPosition, CancellationToken, Task> onCheckpoint)
    {
        _options = _options.WithOnCheckpoint(onCheckpoint);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> OnLive(
        Func<TState, CancellationToken, Task> onLive)
    {
        _options = _options.WithOnLive(onLive);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> OnEventError(
        OnEventErrorCallback<TRawEvent, TPosition, TState> onEventError)
    {
        _options = _options.WithOnEventError(onEventError);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> OnSubscriptionDropped(
        Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> onSubscriptionDropped)
    {
        _options = _options.WithOnSubscriptionDropped(onSubscriptionDropped);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> Map(Action<EventTypeMapBuilder> builderAction)
    {
        var builder = new EventTypeMapBuilder();
        builderAction(builder);
        _options = _options.MergeEventTypes(builder.EventTypeMap);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> When<TEvent>(Action<TEvent, TState> onEvent)
    {
        _options = _options.WithOnEvent(onEvent);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> When<TEvent>(
        Action<TEvent, IReadOnlyDictionary<string, string>, TState> onEvent)
    {
        _options = _options.WithOnEvent(onEvent);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> When<TEvent>(
        Func<TEvent, TState, CancellationToken, Task> onEvent)
    {
        _options = _options.WithOnEvent(onEvent);
        return this;
    }

    public StateProjectionOptionsBuilder<TRawEvent, TPosition, TState> When<TEvent>(
        OnEventCallback<TEvent, TState> onEvent)
    {
        _options = _options.WithOnEvent(onEvent);
        return this;
    }

    public EventStreamConsumerBuilder<TRawEvent, TPosition> And()
    {
        return _consumerBuilder;
    }

    public IEventStreamSubscription Build() => _consumerBuilder.CoreBuilder.Build();

    IEventStreamConsumer<TRawEvent, TPosition> IEventStreamConsumerBuilder<TRawEvent, TPosition>.Build(
        IReadEventAdapter<TRawEvent> readEventAdapter)
    {
        var options = _options.MapMissingEventTypesWithDefaultNames();
        return new StateProjection<TRawEvent, TPosition, TState>(options, readEventAdapter);
    }
}