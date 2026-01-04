namespace DomainBlocks.EventStore.Abstractions;

public static class AppendEvent
{
    public static AppendEvent<TEventBase> Create<TEventBase>(
        TEventBase @event,
        IEnumerable<KeyValuePair<string, string>>? metadata = null) where TEventBase : class
    {
        return new AppendEvent<TEventBase>(@event, metadata);
    }

    public static AppendEvent<TEventBase> Create<TEventBase>(
        TEventBase @event,
        KeyValuePair<string, string>[] metadata) where TEventBase : class
    {
        return new AppendEvent<TEventBase>(@event, metadata);
    }
}

public readonly struct AppendEvent<TEventBase> where TEventBase : class
{
    private readonly KeyValuePair<string, string>[]? _metadata;

    public AppendEvent(TEventBase @event, IEnumerable<KeyValuePair<string, string>>? metadata = null)
    {
        Event = @event;
        _metadata = metadata as KeyValuePair<string, string>[] ?? metadata?.ToArray();
    }

    public AppendEvent(TEventBase @event, KeyValuePair<string, string>[] metadata)
    {
        Event = @event;
        _metadata = metadata;
    }

    public TEventBase Event =>
        field ?? throw new InvalidOperationException(
            "This operation cannot be performed on a default instance of AppendEvent<TEventBase>.");

    public ReadOnlySpan<KeyValuePair<string, string>> Metadata => _metadata;
}