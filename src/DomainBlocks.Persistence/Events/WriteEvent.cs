namespace DomainBlocks.Persistence.Events;

public sealed class WriteEvent : EventBase
{
    public WriteEvent(string name, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte>? metadata) :
        base(name, payload, metadata)
    {
    }
}