using DomainBlocks.EventStore.Internal.Feeds;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IChangeStreamSubject<out TChange>
{
    IDisposable Attach(IFeedObserver<TChange> observer, string correlationId = "unknown");

    Task<IChangeStreamConnection> ConnectAsync(CancellationToken cancellationToken = default);
}