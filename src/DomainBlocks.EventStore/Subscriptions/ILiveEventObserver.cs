namespace DomainBlocks.EventStore.Subscriptions;

public interface ILiveEventObserver<in TLiveEvent>
{
    ValueTask OnNextAsync(TLiveEvent liveEvent, CancellationToken cancellationToken);

    ValueTask OnResetAsync(CancellationToken cancellationToken);

    ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken);
}

public interface ILiveEventFeed
{
}