namespace DomainBlocks.Experimental.Persistence.Events;

public static class ReadEvent
{
    public static ReadEvent<TSerializedData> Create<TSerializedData>(
        string name, TSerializedData payload, TSerializedData? metadata, StreamVersion streamVersion)
    {
        return new ReadEvent<TSerializedData>(name, payload, metadata, streamVersion);
    }
}

public sealed class ReadEvent<TSerializedData> : EventBase<TSerializedData>
{
    public ReadEvent(string name, TSerializedData payload, TSerializedData? metadata, StreamVersion streamVersion) :
        base(name, payload, metadata)
    {
        StreamVersion = streamVersion;
    }

    public StreamVersion StreamVersion { get; }
}