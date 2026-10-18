namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Represents a stored event that an <see cref="EventFilter"/> can evaluate before the event is decoded.
/// </summary>
public interface IFilterableEvent
{
    /// <summary>
    /// Gets the name the event is stored under.
    /// </summary>
    string EventName { get; }

    /// <summary>
    /// Gets the ID of the event's stream.
    /// </summary>
    string StreamId { get; }

    /// <summary>
    /// Gets the time the event was stored.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets the metadata value for the specified key, if the event has one.
    /// </summary>
    /// <remarks>
    /// The value is a span so that an implementation can return it without allocating a string for each lookup, for
    /// example from a buffer that it reuses. It is valid only until the event changes, so a filter compares it at once
    /// and does not keep it.
    /// </remarks>
    bool TryGetMetadata(string key, out ReadOnlySpan<char> value);
}