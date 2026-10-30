namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Creates the sessions of one feed.
/// </summary>
/// <remarks>
/// The feed creates a session only after it has disposed the previous one, so the factory's sessions never run at the
/// same time. Disposing the factory releases anything it holds across sessions, such as a replication slot.
/// </remarks>
internal interface IFeedSessionFactory<T, TMark> : IAsyncDisposable
{
    Task<IFeedSession<T, TMark>> CreateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether a failure to create or read a session is worth retrying.
    /// </summary>
    /// <remarks>
    /// The feed retries creating a session, and creates a new one when a read fails, only for the errors that this
    /// accepts. Any other error faults the feed.
    /// </remarks>
    bool IsTransient(Exception exception);
}