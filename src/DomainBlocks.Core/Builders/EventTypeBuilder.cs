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

    IEventType IEventTypeBuilder.Build()
    {
        var eventName = _eventName ?? typeof(TEvent).Name;
        return new EventType<TEvent, TEventBase>(eventName);
    }
}