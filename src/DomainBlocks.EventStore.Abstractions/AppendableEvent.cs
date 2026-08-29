namespace DomainBlocks.EventStore.Abstractions;

public static class AppendableEvent
{
    public static AppendableEvent<TPayload> Create<TPayload>(
        TPayload payload,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
        where TPayload : notnull
    {
        return new AppendableEvent<TPayload>(payload, metadata);
    }

    public static AppendableEvent<TPayload> Create<TPayload>(TPayload payload, KeyValuePair<string, string>[] metadata)
        where TPayload : notnull
    {
        return new AppendableEvent<TPayload>(payload, metadata);
    }
}

public readonly struct AppendableEvent<TPayload> where TPayload : notnull
{
    private readonly KeyValuePair<string, string>[]? _metadata;

    public AppendableEvent(TPayload payload, IEnumerable<KeyValuePair<string, string>>? metadata = null)
    {
        Payload = payload;
        _metadata = metadata as KeyValuePair<string, string>[] ?? metadata?.ToArray();
    }

    public AppendableEvent(TPayload payload, KeyValuePair<string, string>[] metadata)
    {
        Payload = payload;
        _metadata = metadata;
    }

    public TPayload Payload { get; }
    public ReadOnlySpan<KeyValuePair<string, string>> Metadata => _metadata;
}