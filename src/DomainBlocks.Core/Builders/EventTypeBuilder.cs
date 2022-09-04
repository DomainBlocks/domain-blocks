namespace DomainBlocks.Core.Builders;

public class EventTypeBuilder<TEvent, TEventBase> : IEventTypeBuilder where TEvent : TEventBase
{
    private string _eventName;

    internal EventTypeBuilder()
    {
    }

    public void HasName(string eventName)
    {
        _eventName = eventName;
    }

    IEventType IEventTypeBuilder.Build() => new EventType<TEvent, TEventBase>(_eventName);
}