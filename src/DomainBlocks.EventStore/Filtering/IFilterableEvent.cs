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
    /// The decoded event payload, loaded only when an event type filter needs it, and no cheaper filter has ruled out
    /// the event.
    /// </summary>
    object DecodedPayload { get; }
}