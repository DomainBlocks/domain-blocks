using System;

namespace DomainBlocks.Core;

public sealed class EventOptions<TEvent, TEventBase> : IEventOptions where TEvent : TEventBase
{
    public EventOptions()
    {
    }

    private EventOptions(EventOptions<TEvent, TEventBase> copyFrom)
    {
        EventName = copyFrom.EventName;
    }

    public Type ClrType => typeof(TEvent);
    public string EventName { get; private init; } = typeof(TEvent).Name;

    public EventOptions<TEvent, TEventBase> WithEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be null or empty.", nameof(eventName));

        return new EventOptions<TEvent, TEventBase>(this) { EventName = eventName };
    }
}