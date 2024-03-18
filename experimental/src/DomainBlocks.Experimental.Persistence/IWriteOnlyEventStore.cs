namespace DomainBlocks.Experimental.Persistence;

/// <summary>
/// Exposes write-only operations for a store of events.
/// </summary>
public interface IWriteOnlyEventStore<in TWriteEvent>
{
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
    Task<StreamVersion?> AppendToStreamAsync(
        string streamId,
        IEnumerable<TWriteEvent> events,
        StreamVersion expectedVersion,
        CancellationToken cancellationToken = default);

    Task<StreamVersion?> AppendToStreamAsync(
        string streamId,
        IEnumerable<TWriteEvent> events,
        ExpectedStreamState expectedState = ExpectedStreamState.Any,
        CancellationToken cancellationToken = default);
}