namespace DomainBlocks.V1.Subscriptions;

public interface IEventStreamSubscriptionStatusSource
{
    EventStreamSubscriptionStatus SubscriptionStatus { get; }
}