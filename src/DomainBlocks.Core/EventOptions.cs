using System;

namespace DomainBlocks.Core;

internal sealed class EventOptions<TAggregate, TEventBase> : IEventOptions
{
    private Func<TAggregate, TEventBase, TAggregate> _eventApplier;
    private string _eventName;

    public EventOptions(Type clrType)
    {
        ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
    }

    private EventOptions(EventOptions<TAggregate, TEventBase> copyFrom)
    {
        _eventApplier = copyFrom._eventApplier;
        _eventName = copyFrom._eventName;
        ClrType = copyFrom.ClrType;
    }

    public Type ClrType { get; }
    public string EventName => _eventName ?? ClrType.Name;

    public EventOptions<TAggregate, TEventBase> WithEventApplier(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        return new EventOptions<TAggregate, TEventBase>(this) { _eventApplier = eventApplier };
    }

    public EventOptions<TAggregate, TEventBase> WithEventApplier(Action<TAggregate, TEventBase> eventApplier)
    {
        return new EventOptions<TAggregate, TEventBase>(this)
        {
            _eventApplier = (agg, e) =>
            {
                eventApplier(agg, e);
                return agg;
            }
        };
    }

    public EventOptions<TAggregate, TEventBase> WithEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be null or whitespace.", nameof(eventName));

        return new EventOptions<TAggregate, TEventBase>(this) { _eventName = eventName };
    }

    public TAggregate ApplyEvent(TAggregate aggregate, object @event)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        if (@event.GetType() != ClrType)
        {
            throw new ArgumentException(
                $"Expected event of type {ClrType.Name} but got {@event.GetType().Name}.",
                nameof(@event));
        }

        if (_eventApplier == null)
        {
            throw new InvalidOperationException(
                $"Cannot apply event {ClrType.Name} to aggregate {typeof(TAggregate).Name} as no event applier has " +
                "been specified.");
        }

        return _eventApplier(aggregate, (TEventBase)@event);
    }

    public EventOptions<TAggregate, TEventBase> Merge(EventOptions<TAggregate, TEventBase> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));

        if (other.ClrType != ClrType)
        {
            throw new ArgumentException("Cannot merge event options with for different event type.", nameof(other));
        }

        return new EventOptions<TAggregate, TEventBase>(ClrType)
        {
            _eventApplier = other._eventApplier ?? _eventApplier,
            _eventName = other._eventName ?? _eventName
        };
    }
}

public sealed class EventOptions<TAggregate, TEventBase, TEvent> : IEventOptions where TEvent : TEventBase
{
    private EventOptions<TAggregate, TEventBase> _innerOptions = new(typeof(TEvent));

    public EventOptions()
    {
    }

    private EventOptions(EventOptions<TAggregate, TEventBase, TEvent> copyFrom)
    {
        _innerOptions = copyFrom._innerOptions;
    }

    public Type ClrType => _innerOptions.ClrType;
    public string EventName => _innerOptions.EventName;

    public EventOptions<TAggregate, TEventBase, TEvent> WithEventApplier(
        Func<TAggregate, TEvent, TAggregate> eventApplier)
    {
        return new EventOptions<TAggregate, TEventBase, TEvent>(this)
        {
            _innerOptions = _innerOptions.WithEventApplier((agg, e) => eventApplier(agg, (TEvent)e))
        };
    }

    public EventOptions<TAggregate, TEventBase, TEvent> WithEventApplier(Action<TAggregate, TEvent> eventApplier)
    {
        return new EventOptions<TAggregate, TEventBase, TEvent>(this)
        {
            _innerOptions = _innerOptions.WithEventApplier((agg, e) => eventApplier(agg, (TEvent)e))
        };
    }

    public EventOptions<TAggregate, TEventBase, TEvent> WithEventName(string eventName)
    {
        return new EventOptions<TAggregate, TEventBase, TEvent>(this)
        {
            _innerOptions = _innerOptions.WithEventName(eventName)
        };
    }

    internal EventOptions<TAggregate, TEventBase> HideEventType() => _innerOptions;
}