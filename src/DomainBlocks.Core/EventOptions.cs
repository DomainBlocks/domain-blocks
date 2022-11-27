using System;

namespace DomainBlocks.Core;

public sealed class EventOptions<TAggregate, TEvent> : IEventOptions<TAggregate>
{
    private Func<TAggregate, TEvent, TAggregate> _eventApplier;

    public EventOptions()
    {
    }

    private EventOptions(EventOptions<TAggregate, TEvent> copyFrom)
    {
        _eventApplier = copyFrom._eventApplier;
        EventName = copyFrom.EventName;
    }

    public Type ClrType => typeof(TEvent);
    public string EventName { get; private init; } = typeof(TEvent).Name;

    public EventOptions<TAggregate, TEvent> WithEventApplier(Func<TAggregate, TEvent, TAggregate> eventApplier)
    {
        return new EventOptions<TAggregate, TEvent>(this) { _eventApplier = eventApplier };
    }

    public EventOptions<TAggregate, TEvent> WithEventApplier(Action<TAggregate, TEvent> eventApplier)
    {
        return new EventOptions<TAggregate, TEvent>(this)
        {
            _eventApplier = (agg, e) =>
            {
                eventApplier(agg, e);
                return agg;
            }
        };
    }

    public EventOptions<TAggregate, TEvent> WithEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be null or whitespace.", nameof(eventName));

        return new EventOptions<TAggregate, TEvent>(this) { EventName = eventName };
    }

    public TAggregate ApplyEvent(TAggregate aggregate, object @event)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        if (@event is not TEvent e)
        {
            throw new ArgumentException(
                $"Expected event of type {typeof(TEvent).Name} but got {@event.GetType().Name}.",
                nameof(@event));
        }

        if (_eventApplier == null)
        {
            throw new InvalidOperationException(
                $"Cannot apply event {typeof(TEvent).Name} to aggregate {typeof(TAggregate).Name} as no event " +
                "applier has been specified.");
        }

        return _eventApplier(aggregate, e);
    }
}