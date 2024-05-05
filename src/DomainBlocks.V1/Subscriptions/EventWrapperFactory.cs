using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public delegate IEventWrapper EventWrapperFactory(object @event, SubscriptionPosition position);