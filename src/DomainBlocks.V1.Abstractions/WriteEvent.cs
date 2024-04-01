namespace DomainBlocks.V1.Abstractions;

public sealed class WriteEvent : EventBase
{
    public WriteEvent(string name, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte>? metadata) :
        base(name, payload, metadata)
    {
    }
}