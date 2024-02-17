namespace DomainBlocks.Experimental.EventSourcing.Persistence;

/// <summary>
/// Exposes write-only operations for a repository of events.
/// </summary>
public interface IWriteOnlyEventRepository<in TWriteEvent, TStreamVersion> where TStreamVersion : struct
{
    /// <summary>
    /// Gets a value representing the version to use when a stream is expected to not exist.
    /// </summary>
    TStreamVersion NoStreamVersion { get; }

    /// <summary>
    /// Appends a collection of events to a stream, optionally with an expected stream version for an optimistic
    /// concurrency check.
    /// </summary>
    /// <param name="streamId">The ID of the stream to append the events to.</param>
    /// <param name="events">The events to append.</param>
    /// <param name="expectedVersion">
    /// The optional expected version of the stream. If specified and non-null, and the stream version does not match,
    /// then an exception is thrown. If not specified or null, then the events are appended regardless of the stream's
    /// current version. If no stream exists and <paramref name="expectedVersion"/> is not specified or null, then a new
    /// stream is created.
    /// </param>
    /// <param name="cancellationToken">The optional <see cref="System.Threading.CancellationToken" />.</param>
    /// <returns>
    /// The new version of the stream after appending the events. Null if no events are written, i.e., if the
    /// <paramref name="events"/> collection is empty.
    /// </returns>
    Task<TStreamVersion?> AppendToStreamAsync(
        string streamId,
        IEnumerable<TWriteEvent> events,
        TStreamVersion? expectedVersion = null,
        CancellationToken cancellationToken = default);
}