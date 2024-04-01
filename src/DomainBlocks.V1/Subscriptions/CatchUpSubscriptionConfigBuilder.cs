namespace DomainBlocks.V1.Subscriptions;

public class CatchUpSubscriptionConfigBuilder
{
    public CatchUpSubscriptionConfigBuilder SubscribeToAll()
    {
        return this;
    }

    public CatchUpSubscriptionConfigBuilder AddConsumer<TConsumer>() where TConsumer : ICatchUpSubscriptionConsumer
    {
        return this;
    }

    public EventStreamSubscriptionConfig Build()
    {
        return new EventStreamSubscriptionConfig();
    }
}