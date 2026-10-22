using DomainBlocks.EventStore.PostgreSQL.Feeds;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit.Feeds;

/// <summary>
/// A session factory that opens sessions with a delegate and holds nothing across them.
/// </summary>
internal sealed class DelegateSessionFactory<T>(Func<CancellationToken, Task<IEventLogSession<T>>> open) :
    IEventLogSessionFactory<T>
{
    public int DisposeCount { get; private set; }

    public Task<IEventLogSession<T>> OpenAsync(CancellationToken cancellationToken) => open(cancellationToken);

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}