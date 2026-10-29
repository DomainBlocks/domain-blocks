namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Opens the sessions of one feed.
/// </summary>
/// <remarks>
/// The feed opens a session only after it has disposed the previous one, so the factory's sessions never run at the
/// same time. Consecutive sessions can therefore share state, such as a replication slot, which lets a session resume
/// where the previous one stopped. Disposing the factory releases that state.
/// </remarks>
internal interface IEventLogSessionFactory<T> : IAsyncDisposable
{
    Task<IEventLogSession<T>> OpenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether a failure to open or read a session is worth retrying.
    /// </summary>
    /// <remarks>
    /// The feed retries opening a session, and opens a new one when a read fails, only for the errors that this
    /// accepts. Any other error faults the feed.
    /// </remarks>
    bool IsTransient(Exception exception);
}