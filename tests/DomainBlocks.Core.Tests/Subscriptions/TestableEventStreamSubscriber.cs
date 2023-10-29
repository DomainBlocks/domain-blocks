using DomainBlocks.Core.Subscriptions;
using Moq;

namespace DomainBlocks.Core.Tests.Subscriptions;

public class TestableEventStreamSubscriber<TEvent, TPosition> : IEventStreamSubscriber<TEvent, TPosition>
    where TPosition : struct
{
    private Func<CancellationToken, Task>? _onCatchingUp;
    private Func<TEvent, TPosition, CancellationToken, Task>? _onEvent;
    private Func<CancellationToken, Task>? _onLive;
    private Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task>? _onSubscriptionDropped;
    private IDisposable? _subscribeDisposable;
    private TaskCompletionSource<IDisposable>? _subscribeCompletionSource;

    public TPosition? StartPosition { get; private set; }

    public Task<IDisposable> Subscribe(
        TPosition? fromPositionExclusive,
        Func<CancellationToken, Task> onCatchingUp,
        Func<TEvent, TPosition, CancellationToken, Task> onEvent,
        Func<CancellationToken, Task> onLive,
        Func<SubscriptionDroppedReason, Exception?, CancellationToken, Task> onSubscriptionDropped,
        CancellationToken cancellationToken)
    {
        StartPosition = fromPositionExclusive;
        _onCatchingUp = onCatchingUp;
        _onEvent = onEvent;
        _onLive = onLive;
        _onSubscriptionDropped = onSubscriptionDropped;
        _subscribeCompletionSource = new TaskCompletionSource<IDisposable>();
        _subscribeDisposable = new Mock<IDisposable>().Object;
        return _subscribeCompletionSource.Task;
    }

    public Task InvokeOnCatchingUp() => _onCatchingUp!(CancellationToken.None);

    public Task InvokeOnEvent(TEvent @event, TPosition position) => _onEvent!(@event, position, CancellationToken.None);

    public Task InvokeOnLive() => _onLive!(CancellationToken.None);

    public Task InvokeOnSubscriptionDropped(SubscriptionDroppedReason reason, Exception? exception) =>
        _onSubscriptionDropped!(reason, exception, CancellationToken.None);

    public void CompleteSubscribing() => _subscribeCompletionSource!.SetResult(_subscribeDisposable!);
}