namespace DomainBlocks.Core.Subscriptions;

public interface IEventStreamSubscriber<out TEvent, TPosition> where TPosition : struct
{
    Task<IDisposable> Subscribe(
        IEventStreamListener<TEvent, TPosition> listener,
        TPosition? fromPositionExclusive,
        CancellationToken cancellationToken);
}