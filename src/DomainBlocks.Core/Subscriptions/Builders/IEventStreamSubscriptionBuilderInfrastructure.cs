namespace DomainBlocks.Core.Subscriptions.Builders;

public interface IEventStreamSubscriptionBuilderInfrastructure
{
    void WithSubscriptionFactory(Func<IEventStreamSubscription> subscriptionFactory);
}