namespace DomainBlocks.Core.Subscriptions;

public interface IEventStreamSubscriber<out TEvent, TPosition> where TPosition : struct
{
    Task<IDisposable> Subscribe(
        TPosition? fromPositionExclusive,
        Func<CancellationToken, Task> onCatchingUp,
        Func<TEvent, TPosition, CancellationToken, Task> onEvent,
        Func<CancellationToken, Task> onLive,
        Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> onSubscriptionDropped,
        CancellationToken cancellationToken);
}