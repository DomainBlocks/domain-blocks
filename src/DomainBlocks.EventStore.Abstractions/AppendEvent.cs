namespace DomainBlocks.EventStore.Abstractions;

public static class AppendEvent
{
    public static AppendEvent<TEvent> Create<TEvent>(
        TEvent @event,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
        where TEvent : notnull
    {
        return new AppendEvent<TEvent>(@event, metadata);
    }

    public static AppendEvent<TEvent> Create<TEvent>(TEvent @event, KeyValuePair<string, string>[] metadata)
        where TEvent : notnull
    {
        return new AppendEvent<TEvent>(@event, metadata);
    }
}

public readonly struct AppendEvent<TEvent> where TEvent : notnull
{
    private readonly KeyValuePair<string, string>[]? _metadata;

    public AppendEvent(TEvent @event, IEnumerable<KeyValuePair<string, string>>? metadata = null)
    {
        Event = @event;
        _metadata = metadata as KeyValuePair<string, string>[] ?? metadata?.ToArray();
    }

    public AppendEvent(TEvent @event, KeyValuePair<string, string>[] metadata)
    {
        Event = @event;
        _metadata = metadata;
    }

    public TEvent Event { get; }
    public ReadOnlySpan<KeyValuePair<string, string>> Metadata => _metadata;
}