namespace DomainBlocks.Experimental.Persistence.Events;

public sealed class WriteEvent : EventBase
{
    public WriteEvent(string name, BytesEventData bytesData) : base(name, bytesData)
    {
    }

    public WriteEvent(string name, StringEventData stringData) : base(name, stringData)
    {
    }
}