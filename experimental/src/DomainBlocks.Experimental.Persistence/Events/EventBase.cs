namespace DomainBlocks.Experimental.Persistence.Events;

public abstract class EventBase<TSerializedData>
{
    protected EventBase(string name, TSerializedData payload, TSerializedData? metadata)
    {
        Name = name;
        Payload = payload;
        Metadata = metadata;
    }

    public string Name { get; }
    public TSerializedData Payload { get; }
    public TSerializedData? Metadata { get; }
}