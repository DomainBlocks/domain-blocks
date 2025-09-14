using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Abstractions;

public sealed class NewEventHeader
{
    private readonly ImmutableDictionary<string, string> _metadata;

    public NewEventHeader(string? eventName = null, IEnumerable<KeyValuePair<string, string>>? metadata = null) :
        this(eventName, metadata?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty)
    {
    }

    private NewEventHeader(string? eventName, ImmutableDictionary<string, string> metadata)
    {
        if (eventName != null && string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be whitespace.", nameof(eventName));

        EventName = eventName;
        _metadata = metadata;
    }

    public string? EventName { get; }

    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    public NewEventHeader WithEventName(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        return new NewEventHeader(eventName, _metadata);
    }

    public NewEventHeader WithMetadata(string key, string value) => new(EventName, _metadata.SetItem(key, value));

    public NewEventHeader WithMetadata(params IEnumerable<KeyValuePair<string, string>> entries)
    {
        return new NewEventHeader(EventName, _metadata.SetItems(entries));
    }

    public string GetEventNameOrThrow() => EventName ?? throw new InvalidOperationException("Event name is not set.");
}