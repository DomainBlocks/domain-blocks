namespace DomainBlocks.Core;

internal sealed class EventOptions<TAggregate> : IEventOptions
{
    private Func<TAggregate, object, TAggregate>? _eventApplier;
    private string? _eventName;

    public EventOptions(Type clrType)
    {
        ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
    }

    private EventOptions(EventOptions<TAggregate> copyFrom)
    {
        _eventApplier = copyFrom._eventApplier;
        _eventName = copyFrom._eventName;
        ClrType = copyFrom.ClrType;
    }

    public Type ClrType { get; }
    public string EventName => _eventName ?? ClrType.Name;
    public bool HasEventApplier => _eventApplier != null;

    public EventOptions<TAggregate> WithEventApplier(Func<TAggregate, object, TAggregate> eventApplier)
    {
        return new EventOptions<TAggregate>(this) { _eventApplier = eventApplier };
    }

    public EventOptions<TAggregate> WithEventApplier(Action<TAggregate, object> eventApplier)
    {
        return new EventOptions<TAggregate>(this)
        {
            _eventApplier = (agg, e) =>
            {
                eventApplier(agg, e);
                return agg;
            }
        };
    }

    public EventOptions<TAggregate> WithEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be null or whitespace.", nameof(eventName));

        return new EventOptions<TAggregate>(this) { _eventName = eventName };
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

        return _eventApplier(aggregate, @event);
    }

    public EventOptions<TAggregate> Merge(EventOptions<TAggregate> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));

        if (other.ClrType != ClrType)
        {
            throw new ArgumentException("Cannot merge event options with for different event type.", nameof(other));
        }

        return new EventOptions<TAggregate>(ClrType)
        {
            _eventApplier = other._eventApplier ?? _eventApplier,
            _eventName = other._eventName ?? _eventName
        };
    }
}

public sealed class EventOptions<TAggregate, TEventBase, TEvent> : IEventOptions where TEvent : TEventBase
{
    private EventOptions<TAggregate> _typeErasedOptions = new(typeof(TEvent));

    public EventOptions()
    {
    }

    private EventOptions(EventOptions<TAggregate, TEventBase, TEvent> copyFrom)
    {
        _typeErasedOptions = copyFrom._typeErasedOptions;
    }

    public Type ClrType => _typeErasedOptions.ClrType;
    public string EventName => _typeErasedOptions.EventName;

    public EventOptions<TAggregate, TEventBase, TEvent> WithEventApplier(
        Func<TAggregate, TEvent, TAggregate> eventApplier)
    {
        return new EventOptions<TAggregate, TEventBase, TEvent>(this)
        {
            _typeErasedOptions = _typeErasedOptions.WithEventApplier((agg, e) => eventApplier(agg, (TEvent)e))
        };
    }

    public EventOptions<TAggregate, TEventBase, TEvent> WithEventApplier(Action<TAggregate, TEvent> eventApplier)
    {
        return new EventOptions<TAggregate, TEventBase, TEvent>(this)
        {
            _typeErasedOptions = _typeErasedOptions.WithEventApplier((agg, e) => eventApplier(agg, (TEvent)e))
        };
    }

    public EventOptions<TAggregate, TEventBase, TEvent> WithEventName(string eventName)
    {
        return new EventOptions<TAggregate, TEventBase, TEvent>(this)
        {
            _typeErasedOptions = _typeErasedOptions.WithEventName(eventName)
        };
    }

    internal EventOptions<TAggregate> HideEventType() => _typeErasedOptions;
}