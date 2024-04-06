namespace DomainBlocks.V1.Abstractions;

public sealed class WritableEventEntry : EventEntryBase
{
    public WritableEventEntry(string name, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte>? metadata) :
        base(name, payload, metadata)
    {
    }
}