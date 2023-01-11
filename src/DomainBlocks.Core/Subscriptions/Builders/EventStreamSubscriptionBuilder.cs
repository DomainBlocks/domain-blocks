namespace DomainBlocks.Core.Subscriptions.Builders;

public class EventStreamSubscriptionBuilder : IEventStreamSubscriptionBuilderInfrastructure
{
    private Func<IEventStreamSubscription>? _subscriptionFactory;

    void IEventStreamSubscriptionBuilderInfrastructure.WithSubscriptionFactory(
        Func<IEventStreamSubscription> subscriptionFactory)
    {
        _subscriptionFactory = subscriptionFactory;
    }

    public IEventStreamSubscription Build()
    {
        if (_subscriptionFactory == null)
        {
            throw new InvalidOperationException(
                "Cannot create event stream subscription as no factory has been specified");
        }

        return _subscriptionFactory();
    }
}