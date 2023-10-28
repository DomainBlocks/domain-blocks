using DomainBlocks.Core.Subscriptions;

namespace DomainBlocks.Core.Tests.Subscriptions.Testing;

public class TestableEventStreamSubscription : EventStreamSubscriptionBase<string, int>
{
    private readonly List<string> _catchupEvents = new();

    public TestableEventStreamSubscription(IEnumerable<IEventStreamConsumer<string, int>> consumers) :
        base(consumers)
    {
    }

    public TestableDisposable? CurrentSubscriptionDisposable { get; private set; }
    public int? StartPositionSubscribedFrom { get; private set; }

    public void SetCatchupEvents(params string[] events)
    {
        _catchupEvents.Clear();
        _catchupEvents.AddRange(events);
    }

    public new Task NotifyCatchingUp() => base.NotifyCatchingUp();

    public new Task NotifyEvent(string @event, int position, CancellationToken cancellationToken = default) =>
        base.NotifyEvent(@event, position, cancellationToken);

    public new Task NotifyLive() => base.NotifyLive();

    protected override async Task<IDisposable> Subscribe(
        int? fromPositionExclusive,
        CancellationToken cancellationToken)
    {
        StartPositionSubscribedFrom = fromPositionExclusive;

        for (var i = 0; i < _catchupEvents.Count; i++)
        {
            await base.NotifyEvent(_catchupEvents[i], i, cancellationToken);
        }

        CurrentSubscriptionDisposable = new TestableDisposable();
        return CurrentSubscriptionDisposable;
    }
}