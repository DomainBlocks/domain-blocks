namespace DomainBlocks.Experimental.Persistence.Events;

public static class WriteEvent
{
    public static WriteEvent<TSerializedData> Create<TSerializedData>(
        string name, TSerializedData payload, TSerializedData? metadata)
    {
        return new WriteEvent<TSerializedData>(name, payload, metadata);
    }
}

public sealed class WriteEvent<TSerializedData> : EventBase<TSerializedData>
{
    public WriteEvent(string name, TSerializedData payload, TSerializedData? metadata) : base(name, payload, metadata)
    {
    }
}