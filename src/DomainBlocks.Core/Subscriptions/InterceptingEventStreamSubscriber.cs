namespace DomainBlocks.Core.Subscriptions;

public sealed class InterceptingEventStreamSubscriber<TEvent, TPosition> : IEventStreamSubscriber<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly IEventStreamSubscriber<TEvent, TPosition> _target;
    private readonly IEventStreamSubscriberInterceptor<TEvent, TPosition> _interceptor;

    public InterceptingEventStreamSubscriber(
        IEventStreamSubscriber<TEvent, TPosition> target,
        IEventStreamSubscriberInterceptor<TEvent, TPosition> interceptor)
    {
        _target = target;
        _interceptor = interceptor;
    }

    public CheckpointFrequency CatchUpCheckpointFrequency => _target.CatchUpCheckpointFrequency;

    public CheckpointFrequency LiveCheckpointFrequency => _target.LiveCheckpointFrequency;

    public Task<TPosition?> OnStarting(CancellationToken cancellationToken) =>
        _interceptor.OnStarting(ct => _target.OnStarting(ct), cancellationToken);

    public Task OnCatchingUp(CancellationToken cancellationToken) =>
        _interceptor.OnCatchingUp(ct => _target.OnCatchingUp(ct), cancellationToken);

    public Task<OnEventResult> OnEvent(TEvent @event, TPosition position, CancellationToken cancellationToken) =>
        _interceptor.OnEvent(@event, position, ct => _target.OnEvent(@event, position, ct), cancellationToken);

    public Task OnCheckpoint(TPosition position, CancellationToken cancellationToken) =>
        _interceptor.OnCheckpoint(position, ct => _target.OnCheckpoint(position, ct), cancellationToken);

    public Task OnLive(CancellationToken cancellationToken) =>
        _interceptor.OnLive(ct => _target.OnLive(ct), cancellationToken);

    public Task<EventErrorResolution> OnEventError(
        TEvent @event,
        TPosition position,
        Exception exception,
        CancellationToken cancellationToken) => _target.OnEventError(@event, position, exception, cancellationToken);

    public Task OnSubscriptionDropped(
        SubscriptionDroppedReason reason,
        Exception? exception,
        CancellationToken cancellationToken) => _target.OnSubscriptionDropped(reason, exception, cancellationToken);
}