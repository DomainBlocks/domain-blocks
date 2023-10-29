namespace DomainBlocks.Core.Subscriptions;

public interface IEventStreamSubscribable<out TEvent, TPosition> where TPosition : struct
{
    Task<IDisposable> Subscribe(
        IEventStreamSubscriber<TEvent, TPosition> subscriber,
        TPosition? fromPositionExclusive = null,
        CancellationToken cancellationToken = default);
}