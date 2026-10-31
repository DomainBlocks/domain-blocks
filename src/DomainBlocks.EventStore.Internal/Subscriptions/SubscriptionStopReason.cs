namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Specifies why a <see cref="SubscriptionObserver{TEvent,TSourceEvent}"/> stopped.
/// </summary>
internal enum SubscriptionStopReason
{
    None,
    QueueOverflow,
    FeedReset,
    Disposed
}