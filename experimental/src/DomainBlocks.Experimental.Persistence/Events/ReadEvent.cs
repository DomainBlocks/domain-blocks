namespace DomainBlocks.Experimental.Persistence.Events;

public sealed class ReadEvent : EventBase
{
    public ReadEvent(
        string name, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte>? metadata, StreamVersion streamVersion) :
        base(name, payload, metadata)
    {
        StreamVersion = streamVersion;
    }

    public StreamVersion StreamVersion { get; }
}