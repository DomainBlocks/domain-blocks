namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Commits append requests to the event log.
/// </summary>
internal interface IAppender : IAsyncDisposable
{
    /// <summary>
    /// Appends the request and waits for the outcome. Throws <see cref="TimeoutException"/> if the outcome is not
    /// known within <paramref name="timeout"/>, in which case the request may still be committed later.
    /// </summary>
    Task AppendAsync(AppendRequest request, TimeSpan timeout, CancellationToken cancellationToken);
}
