namespace DomainBlocks.Experimental.Persistence.Events;

public sealed class StringEventData
{
    public StringEventData(string payload, string? metadata)
    {
        Payload = payload;
        Metadata = metadata;
    }

    public string Payload { get; }
    public string? Metadata { get; }
}