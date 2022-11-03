using System;

namespace DomainBlocks.Core;

public class EventType<TEvent, TEventBase> : IEventType where TEvent : TEventBase
{
    public EventType()
    {
    }
    
    private EventType(EventType<TEvent, TEventBase> copyFrom)
    {
        EventName = copyFrom.EventName;
    }

    public Type ClrType => typeof(TEvent);
    public string EventName { get; private init; } = typeof(TEvent).Name;

    public EventType<TEvent, TEventBase> WithEventName(string eventName)
    {
        return new EventType<TEvent, TEventBase>(this) { EventName = eventName };
    }
}