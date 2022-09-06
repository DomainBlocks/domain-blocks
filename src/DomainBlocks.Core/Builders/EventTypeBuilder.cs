using System;

namespace DomainBlocks.Core.Builders;

public class EventTypeBuilder<TEvent, TEventBase> : IEventTypeBuilder where TEvent : TEventBase
{
    private string _eventName;

    public void HasName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be null or whitespace.", nameof(eventName));

        _eventName = eventName;
    }

    IEventType IEventTypeBuilder.Build()
    {
        var eventName = _eventName ?? typeof(TEvent).Name;
        return new EventType<TEvent, TEventBase>(eventName);
    }
}