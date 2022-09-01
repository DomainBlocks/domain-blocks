namespace DomainBlocks.Persistence.New.Builders;

public class EventTypeBuilder<TEvent, TEventBase> : IEventTypeBuilder where TEvent : TEventBase
{
    private string _eventName;

    public EventTypeBuilder<TEvent, TEventBase> HasName(string eventName)
    {
        _eventName = eventName;
        return this;
    }

    public EventType<TEvent, TEventBase> Build()
    {
        return new EventType<TEvent, TEventBase>(_eventName);
    }

    IEventType IEventTypeBuilder.Build() => Build();
}