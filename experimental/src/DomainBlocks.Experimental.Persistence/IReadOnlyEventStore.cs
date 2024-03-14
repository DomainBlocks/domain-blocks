namespace DomainBlocks.Experimental.Persistence;

/// <summary>
/// Exposes read-only operations for a store of events.
/// </summary>
public interface IReadOnlyEventStore<out TReadEvent, TStreamVersion> where TStreamVersion : struct
{
    /// <summary>
    /// Reads an event stream forwards or backwards, optionally from a specific version within the stream.
    /// </summary>
    /// <param name="direction">The direction to read, i.e., forwards or backwards.</param>
    /// <param name="streamId">The ID of the stream to read.</param>
    /// <param name="fromVersion">
    /// The optional version to start reading from. If not specified or null, then the stream will be read from the
    /// start in the forwards direction, or from the end in the backwards direction.
    /// </param>
    /// <param name="cancellationToken">The optional <see cref="System.Threading.CancellationToken" />.</param>
    /// <returns>An async enumerable of <see cref="TReadEvent" />.</returns>
    IAsyncEnumerable<TReadEvent> ReadStreamAsync(
        ReadStreamDirection direction,
        string streamId,
        TStreamVersion? fromVersion = null,
        CancellationToken cancellationToken = default);
}