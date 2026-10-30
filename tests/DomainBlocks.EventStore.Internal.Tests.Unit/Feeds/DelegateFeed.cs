using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Feeds;

/// <summary>
/// A feed that creates sessions with a delegate and holds nothing across them. It counts how often its own part of
/// disposal runs, and can run a callback at that point.
/// </summary>
internal sealed class DelegateFeed(
    Func<CancellationToken, Task<IFeedSession<long, long>>> factory,
    FeedResumePolicy resumePolicy,
    ILogger? logger = null,
    Action? onDisposing = null) :
    EventLogFeed<long, long>(resumePolicy, logger)
{
    public int DisposeCount { get; private set; }

    protected override Task<IFeedSession<long, long>> CreateSessionAsync(CancellationToken cancellationToken) =>
        factory(cancellationToken);

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();

        DisposeCount++;
        onDisposing?.Invoke();
    }
}