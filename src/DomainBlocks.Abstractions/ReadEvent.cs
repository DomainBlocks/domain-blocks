namespace DomainBlocks.Abstractions;

public sealed class ReadEvent : EventBase
{
    public ReadEvent(
        string name,
        ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte>? metadata,
        StreamVersion streamVersion,
        GlobalPosition globalPosition) : base(name, payload, metadata)
    {
        StreamVersion = streamVersion;
        GlobalPosition = globalPosition;
    }

    public StreamVersion StreamVersion { get; }
    public GlobalPosition GlobalPosition { get; }
}