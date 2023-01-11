using DomainBlocks.Core.Serialization;
using DomainBlocks.Core.Subscriptions;
using DomainBlocks.Core.Subscriptions.Builders;

namespace DomainBlocks.Core.Projections.Experimental.Builders;

public sealed class EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> :
    IEventStreamSubscriberBuilder<TRawEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly EventStreamSubscriberBuilder<TRawEvent, TPosition> _subscriberBuilder;
    private EventHandlerProjectionOptions<TRawEvent, TPosition> _options = new();

    internal EventHandlerProjectionOptionsBuilder(
        EventStreamSubscriberBuilder<TRawEvent, TPosition> subscriberBuilder)
    {
        _subscriberBuilder = subscriberBuilder;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> WithCheckpoints(
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

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> WithCatchUpCheckpoints(
        Action<CheckpointFrequencyBuilder> builderAction)
    {
        var builder = new CheckpointFrequencyBuilder();
        builderAction(builder);
        _options = _options.WithCatchupCheckpointFrequency(builder.Build());
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> WithLiveCheckpoints(
        Action<CheckpointFrequencyBuilder> builderAction)
    {
        var builder = new CheckpointFrequencyBuilder();
        builderAction(builder);
        _options = _options.WithLiveCheckpointFrequency(builder.Build());
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> OnStarting(
        Func<CancellationToken, Task<TPosition?>> onStarting)
    {
        _options = _options.WithOnStarting(onStarting);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> OnCatchingUp(
        Func<CancellationToken, Task> onCatchingUp)
    {
        _options = _options.WithOnCatchingUp(onCatchingUp);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> OnCheckpoint(
        Func<TPosition, CancellationToken, Task> onCheckpoint)
    {
        _options = _options.WithOnCheckpoint(onCheckpoint);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> OnLive(Func<CancellationToken, Task> onLive)
    {
        _options = _options.WithOnLive(onLive);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> OnEventError(
        OnEventErrorCallback<TRawEvent, TPosition> onEventError)
    {
        _options = _options.WithOnEventError(onEventError);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> OnSubscriptionDropped(
        Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> onSubscriptionDropped)
    {
        _options = _options.WithOnSubscriptionDropped(onSubscriptionDropped);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> Map(Action<EventTypeMapBuilder> builderAction)
    {
        var builder = new EventTypeMapBuilder();
        builderAction(builder);
        _options = _options.MergeEventTypes(builder.EventTypeMap);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> When<TEvent>(Action<TEvent> onEvent)
    {
        _options = _options.WithOnEvent(onEvent);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> When<TEvent>(
        Action<TEvent, IReadOnlyDictionary<string, string>> onEvent)
    {
        _options = _options.WithOnEvent(onEvent);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> When<TEvent>(
        Func<TEvent, CancellationToken, Task> onEvent)
    {
        _options = _options.WithOnEvent(onEvent);
        return this;
    }

    public EventHandlerProjectionOptionsBuilder<TRawEvent, TPosition> When<TEvent>(OnEventCallback<TEvent> onEvent)
    {
        _options = _options.WithOnEvent(onEvent);
        return this;
    }

    public EventStreamSubscriberBuilder<TRawEvent, TPosition> And()
    {
        return _subscriberBuilder;
    }

    public IEventStreamSubscription Build() => _subscriberBuilder.CoreBuilder.Build();

    IEventStreamSubscriber<TRawEvent, TPosition> IEventStreamSubscriberBuilder<TRawEvent, TPosition>.Build(
        IReadEventAdapter<TRawEvent> readEventAdapter)
    {
        var options = _options.MapMissingEventTypesWithDefaultNames();
        return new EventHandlerProjectionSubscriber<TRawEvent, TPosition>(options, readEventAdapter);
    }
}