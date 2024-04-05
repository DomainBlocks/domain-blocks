namespace DomainBlocks.V1.Abstractions;

public sealed class WriteEventRecord : EventRecordBase
{
    public WriteEventRecord(string name, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte>? metadata) :
        base(name, payload, metadata)
    {
    }
}