using System;

namespace DomainBlocks.Core;

public class EventOptions<TEvent, TEventBase> : IEventOptions where TEvent : TEventBase
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
        return new EventOptions<TEvent, TEventBase>(this) { EventName = eventName };
    }
}