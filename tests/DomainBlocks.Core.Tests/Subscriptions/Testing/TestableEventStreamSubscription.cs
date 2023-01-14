using DomainBlocks.Core.Subscriptions;

namespace DomainBlocks.Core.Tests.Subscriptions.Testing;

public class TestableEventStreamSubscription : EventStreamSubscriptionBase<string, int>
{
    public TestableEventStreamSubscription(IEventStreamSubscriber<string, int> subscriber) : base(subscriber)
    {
    }

    public TestableDisposable? CurrentSubscriptionDisposable { get; private set; }

    public new Task NotifyCatchingUp() => base.NotifyCatchingUp();

    public new Task NotifyEvent(string @event, int position, CancellationToken cancellationToken = default) =>
        base.NotifyEvent(@event, position, cancellationToken);

    public new Task NotifyLive() => base.NotifyLive();

    protected override Task<IDisposable> Subscribe(int? fromPositionExclusive, CancellationToken cancellationToken)
    {
        CurrentSubscriptionDisposable = new TestableDisposable();
        return Task.FromResult<IDisposable>(CurrentSubscriptionDisposable);
    }
}