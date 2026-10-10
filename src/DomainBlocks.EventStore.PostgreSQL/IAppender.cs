namespace DomainBlocks.EventStore.PostgreSQL;

internal interface IAppender : IAsyncDisposable
{
    /// <summary>
    /// Appends the request and waits for the outcome. If the wait ends with a <see cref="TimeoutException"/>, the
    /// request may still commit later.
    /// </summary>
    Task AppendAsync(AppendRequest request, TimeSpan timeout, CancellationToken cancellationToken);
}