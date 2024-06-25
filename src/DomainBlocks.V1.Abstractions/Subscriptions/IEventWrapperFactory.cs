namespace DomainBlocks.V1.Abstractions.Subscriptions;

public interface IEventWrapperFactory
{
    IEventWrapper Create(object @event, SubscriptionPosition position);
}