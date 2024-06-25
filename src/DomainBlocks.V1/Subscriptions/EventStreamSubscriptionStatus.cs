namespace DomainBlocks.V1.Subscriptions;

public enum EventStreamSubscriptionStatus
{
    Unsubscribed,
    CatchingUp,
    Live
}