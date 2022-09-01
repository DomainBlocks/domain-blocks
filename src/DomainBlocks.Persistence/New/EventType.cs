using System;

namespace DomainBlocks.Persistence.New;

public class EventType<TEvent, TEventBase> : IEventType where TEvent : TEventBase
{
    public EventType(string eventName)
    {
        EventName = eventName;
    }

    public Type ClrType => typeof(TEvent);
    public string EventName { get; }
}