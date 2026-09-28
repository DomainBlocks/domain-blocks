using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Represents an event in its stored form, so a store can evaluate filters before decoding the event.
/// </summary>
public interface IFilterableEvent
{
    string EventName { get; }

    string StreamId { get; }

    DateTimeOffset CreatedAt { get; }

    bool TryGetMetadata(string key, [MaybeNullWhen(false)] out string value);

    /// <summary>
    /// Gets the decoded event payload when an event type filter requires it.
    /// </summary>
    object DecodedPayload { get; }
}