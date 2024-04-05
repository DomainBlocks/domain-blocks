namespace DomainBlocks.V1.Abstractions;

public sealed class ReadEventRecord : EventRecordBase
{
    public ReadEventRecord(
        string name,
        ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte>? metadata,
        StreamPosition streamPosition,
        GlobalPosition globalPosition) : base(name, payload, metadata)
    {
        StreamPosition = streamPosition;
        GlobalPosition = globalPosition;
    }

    public StreamPosition StreamPosition { get; }
    public GlobalPosition GlobalPosition { get; }
}