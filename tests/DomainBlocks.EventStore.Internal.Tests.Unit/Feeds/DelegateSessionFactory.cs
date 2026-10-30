using DomainBlocks.EventStore.Internal.Feeds;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Feeds;

/// <summary>
/// A session factory that creates sessions with a delegate and holds nothing across them.
/// </summary>
internal sealed class DelegateSessionFactory<T>(
    Func<CancellationToken, Task<IFeedSession<T, long>>> factory) :
    IFeedSessionFactory<T, long>
{
    public int DisposeCount { get; private set; }

    public Task<IFeedSession<T, long>> CreateAsync(CancellationToken cancellationToken) => factory(cancellationToken);

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}