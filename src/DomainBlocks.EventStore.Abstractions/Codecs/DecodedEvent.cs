namespace DomainBlocks.EventStore.Abstractions.Codecs;

public static class DecodedEvent
{
    public static DecodedEvent<TPayload> Create<TPayload>(
        TPayload payload,
        IReadOnlyDictionary<string, string> metadata)
        where TPayload : notnull
    {
        return new DecodedEvent<TPayload>(payload, metadata);
    }
}

public readonly struct DecodedEvent<TPayload>(TPayload payload, IReadOnlyDictionary<string, string> metadata)
    where TPayload : notnull
{
    public TPayload Payload { get; } = payload;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;

    public void Deconstruct(out TPayload payload, out IReadOnlyDictionary<string, string> metadata)
    {
        payload = Payload;
        metadata = Metadata;
    }
}