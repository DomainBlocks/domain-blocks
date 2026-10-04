using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Represents an event in its stored form, so that an <see cref="EventFilter"/> can be evaluated against it before the
/// event is decoded.
/// </summary>
public interface IFilterableEvent
{
    /// <summary>
    /// The name the event is stored under.
    /// </summary>
    string EventName { get; }

    /// <summary>
    /// The ID of the stream the event belongs to.
    /// </summary>
    string StreamId { get; }

    /// <summary>
    /// When the event was stored.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets the value of the metadata entry with the specified key, if the event has one.
    /// </summary>
    bool TryGetMetadata(string key, [MaybeNullWhen(false)] out string value);
}