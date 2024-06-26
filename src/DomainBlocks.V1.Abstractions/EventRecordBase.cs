namespace DomainBlocks.V1.Abstractions;

public abstract class EventRecordBase
{
    protected EventRecordBase(string name, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte>? metadata)
    {
        Name = name;
        Payload = payload;
        Metadata = metadata;
    }

    public string Name { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public ReadOnlyMemory<byte>? Metadata { get; }
}