namespace DomainBlocks.Core.Subscriptions;

public interface IEventStream<out TEvent, TPosition> where TPosition : struct
{
    Task<IDisposable> Subscribe(
        IEventStreamSubscriber<TEvent, TPosition> subscriber,
        TPosition? fromPositionExclusive = null,
        CancellationToken cancellationToken = default);
}