namespace DomainBlocks.V1.Abstractions;

public sealed class WritableEventRecord : EventRecordBase
{
    public WritableEventRecord(string name, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte>? metadata) :
        base(name, payload, metadata)
    {
    }
}