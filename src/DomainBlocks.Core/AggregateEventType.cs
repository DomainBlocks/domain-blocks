namespace DomainBlocks.Core;

internal sealed class AggregateEventType<TAggregate> : IEventType
{
    private Func<TAggregate, object, TAggregate>? _eventApplier;
    private string? _eventName;

    public AggregateEventType(Type clrType)
    {
        ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
    }

    private AggregateEventType(AggregateEventType<TAggregate> copyFrom)
    {
        _eventApplier = copyFrom._eventApplier;
        _eventName = copyFrom._eventName;
        ClrType = copyFrom.ClrType;
    }

    public Type ClrType { get; }
    public string EventName => _eventName ?? ClrType.Name;
    public bool HasEventApplier => _eventApplier != null;

    public AggregateEventType<TAggregate> SetEventApplier(Func<TAggregate, object, TAggregate> eventApplier)
    {
        return new AggregateEventType<TAggregate>(this) { _eventApplier = eventApplier };
    }

    public AggregateEventType<TAggregate> SetEventApplier(Action<TAggregate, object> eventApplier)
    {
        return new AggregateEventType<TAggregate>(this)
        {
            _eventApplier = (agg, e) =>
            {
                eventApplier(agg, e);
                return agg;
            }
        };
    }

    public AggregateEventType<TAggregate> SetEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be null or whitespace.", nameof(eventName));

        return new AggregateEventType<TAggregate>(this) { _eventName = eventName };
    }

    public TAggregate InvokeEventApplier(TAggregate aggregate, object @event)
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

        return _eventApplier(aggregate, @event);
    }

    public AggregateEventType<TAggregate> Merge(AggregateEventType<TAggregate> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));

        if (other.ClrType != ClrType)
        {
            throw new ArgumentException("Cannot merge event options with for different event type.", nameof(other));
        }

        return new AggregateEventType<TAggregate>(ClrType)
        {
            _eventApplier = other._eventApplier ?? _eventApplier,
            _eventName = other._eventName ?? _eventName
        };
    }
}

public sealed class AggregateEventType<TAggregate, TEventBase, TEvent> : IEventType where TEvent : TEventBase
{
    private AggregateEventType<TAggregate> _typeErasedEventType = new(typeof(TEvent));

    public AggregateEventType()
    {
    }

    private AggregateEventType(AggregateEventType<TAggregate, TEventBase, TEvent> copyFrom)
    {
        _typeErasedEventType = copyFrom._typeErasedEventType;
    }

    public Type ClrType => _typeErasedEventType.ClrType;
    public string EventName => _typeErasedEventType.EventName;

    public AggregateEventType<TAggregate, TEventBase, TEvent> SetEventApplier(
        Func<TAggregate, TEvent, TAggregate> eventApplier)
    {
        return new AggregateEventType<TAggregate, TEventBase, TEvent>(this)
        {
            _typeErasedEventType = _typeErasedEventType.SetEventApplier((agg, e) => eventApplier(agg, (TEvent)e))
        };
    }

    public AggregateEventType<TAggregate, TEventBase, TEvent> SetEventApplier(Action<TAggregate, TEvent> eventApplier)
    {
        return new AggregateEventType<TAggregate, TEventBase, TEvent>(this)
        {
            _typeErasedEventType = _typeErasedEventType.SetEventApplier((agg, e) => eventApplier(agg, (TEvent)e))
        };
    }

    public AggregateEventType<TAggregate, TEventBase, TEvent> SetEventName(string eventName)
    {
        return new AggregateEventType<TAggregate, TEventBase, TEvent>(this)
        {
            _typeErasedEventType = _typeErasedEventType.SetEventName(eventName)
        };
    }

    internal AggregateEventType<TAggregate> HideGenericType() => _typeErasedEventType;
}