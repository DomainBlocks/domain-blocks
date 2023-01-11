namespace DomainBlocks.Core.Subscriptions;

public enum SubscriptionDroppedReason
{
    Disposed,
    SubscriberError,
    ServerError
}