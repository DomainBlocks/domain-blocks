namespace DomainBlocks.V1.Subscriptions;

public class EventStreamSubscriptionConfigBuilder
{
    public EventStreamSubscriptionConfigBuilder SubscribeToAll()
    {
        return this;
    }

    public EventStreamSubscriptionConfigBuilder AddConsumer<TConsumer>() where TConsumer : IEventStreamConsumer
    {
        return this;
    }

    public EventStreamSubscriptionConfig Build()
    {
        return new EventStreamSubscriptionConfig();
    }
}