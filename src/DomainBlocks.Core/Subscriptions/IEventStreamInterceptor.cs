namespace DomainBlocks.Core.Subscriptions;

public interface IEventStreamInterceptor<in TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    Task<TPosition?> OnStarting(
        Func<CancellationToken, Task<TPosition?>> continuation,
        CancellationToken cancellationToken);

    Task OnCatchingUp(Func<CancellationToken, Task> continuation, CancellationToken cancellationToken);

    Task<OnEventResult> OnEvent(
        TEvent @event,
        TPosition position,
        Func<CancellationToken, Task<OnEventResult>> continuation,
        CancellationToken cancellationToken);

    Task OnCheckpoint(
        TPosition position,
        Func<CancellationToken, Task> continuation,
        CancellationToken cancellationToken);

    Task OnLive(Func<CancellationToken, Task> continuation, CancellationToken cancellationToken);
}