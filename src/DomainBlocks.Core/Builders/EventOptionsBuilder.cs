namespace DomainBlocks.Core.Builders;

public sealed class EventOptionsBuilder<TEvent, TEventBase> where TEvent : TEventBase
{
    public EventOptions<TEvent, TEventBase> Options { get; private set; } = new();

    public void HasName(string eventName)
    {
        Options = Options.WithEventName(eventName);
    }
}