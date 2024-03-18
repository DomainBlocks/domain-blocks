namespace DomainBlocks.Experimental.Persistence.Events;

public sealed class ReadEvent : EventBase
{
    public ReadEvent(string name, BytesEventData bytesData, StreamVersion streamVersion) : base(name, bytesData)
    {
        StreamVersion = streamVersion;
    }

    public ReadEvent(string name, StringEventData stringData, StreamVersion streamVersion) : base(name, stringData)
    {
        StreamVersion = streamVersion;
    }

    public StreamVersion StreamVersion { get; }
}