namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Creates the sessions of one feed.
/// </summary>
/// <remarks>
/// The feed creates a session only after it has disposed the previous one, so the factory's sessions never run at the
/// same time. Whoever creates the factory disposes it once the feed's connection has stopped, which releases anything
/// it holds across sessions, such as a replication slot.
/// </remarks>
internal interface IFeedSessionFactory<T, TMark> : IAsyncDisposable
{
    Task<IFeedSession<T, TMark>> CreateAsync(CancellationToken cancellationToken);
}