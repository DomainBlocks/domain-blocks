using DomainBlocks.Persistence.Events;

namespace DomainBlocks.Persistence;

/// <summary>
/// Exposes read-only operations for a store of events.
/// </summary>
public interface IReadOnlyEventStore
{
    /// <summary>
    /// Reads an event stream forwards or backwards, optionally from a specific version within the stream.
    /// </summary>
    /// <param name="direction">The direction to read, i.e., forwards or backwards.</param>
    /// <param name="streamName">The ID of the stream to read.</param>
    /// <param name="fromVersion">
    /// The optional version to start reading from. If not specified or null, then the stream will be read from the
    /// start in the forwards direction, or from the end in the backwards direction.
    /// </param>
    /// <param name="cancellationToken">The optional <see cref="System.Threading.CancellationToken" />.</param>
    /// <returns>An async enumerable of <see cref="ReadEvent" />.</returns>
    IAsyncEnumerable<ReadEvent> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamVersion fromVersion,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamReadOrigin readOrigin = StreamReadOrigin.Default,
        CancellationToken cancellationToken = default);
}