using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Abstractions.Events;

public sealed class UncommittedEventHeader
{
    public static readonly UncommittedEventHeader Empty = new();

    private readonly string? _eventName;
    private readonly ImmutableDictionary<string, string> _metadata;

    public UncommittedEventHeader(
        string? eventName = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null) :
        this(eventName, metadata?.ToImmutableDictionary() ?? [])
    {
    }

    private UncommittedEventHeader(string? eventName, ImmutableDictionary<string, string> metadata)
    {
        if (eventName != null && string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be whitespace.", nameof(eventName));

        _eventName = eventName;
        _metadata = metadata;
    }

    public string EventName => _eventName ?? throw new InvalidOperationException("Event name not set.");

    public bool HasEventName => _eventName != null;

    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    public UncommittedEventHeader WithEventName(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        return new UncommittedEventHeader(eventName, _metadata);
    }

    public UncommittedEventHeader WithMetadata(string key, string value) =>
        new(_eventName, _metadata.SetItem(key, value));

    public UncommittedEventHeader WithMetadata(IEnumerable<KeyValuePair<string, string>> items)
    {
        var builder = _metadata.ToBuilder();
        builder.AddRange(items);
        return new UncommittedEventHeader(_eventName, builder.ToImmutable());
    }
}