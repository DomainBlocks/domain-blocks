namespace DomainBlocks.Core.Subscriptions;

public abstract class EventStreamSubscriberInterceptorBase<TEvent, TPosition> :
    IEventStreamSubscriberInterceptor<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    public virtual Task<TPosition?> OnStarting(
        Func<CancellationToken, Task<TPosition?>> continuation,
        CancellationToken cancellationToken) => continuation(cancellationToken);

    public virtual Task OnCatchingUp(Func<CancellationToken, Task> continuation, CancellationToken cancellationToken) =>
        continuation(cancellationToken);

    public virtual Task<OnEventResult> OnEvent(
        TEvent @event,
        TPosition position,
        Func<CancellationToken, Task<OnEventResult>> continuation,
        CancellationToken cancellationToken) => continuation(cancellationToken);

    public virtual Task OnCheckpoint(
        TPosition position,
        Func<CancellationToken, Task> continuation,
        CancellationToken cancellationToken) => continuation(cancellationToken);

    public virtual Task OnLive(Func<CancellationToken, Task> continuation, CancellationToken cancellationToken) =>
        continuation(cancellationToken);
}