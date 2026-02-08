namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public interface IChangeStreamSubscription<out TDocument> : IAsyncDisposable
{
    Task WaitUntilLiveAsync(CancellationToken cancellationToken = default);

    Task ForEachAsync(
        Func<TDocument, CancellationToken, ValueTask> onNext,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TDocument> ReadAllAsync(CancellationToken cancellationToken = default);
}