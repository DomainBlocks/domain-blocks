using System;

namespace DomainBlocks.Core.Builders;

public class EventTypeBuilder<TEvent, TEventBase> where TEvent : TEventBase
{
    public EventType<TEvent, TEventBase> Options { get; private set; } = new();

    public void HasName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be null or whitespace.", nameof(eventName));

        Options = Options.WithEventName(eventName);
    }
}