namespace DomainBlocks.Experimental.Persistence.Events;

public sealed class BytesEventData
{
    public BytesEventData(ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte>? metadata)
    {
        Payload = payload;
        Metadata = metadata;
    }

    public ReadOnlyMemory<byte> Payload { get; }
    public ReadOnlyMemory<byte>? Metadata { get; }
}