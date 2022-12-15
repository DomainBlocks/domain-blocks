using System;

namespace DomainBlocks.Projections.Builders;

public class EventCatchUpSubscriptionOptionsBuilder : IEventCatchUpSubscriptionOptionsBuilderInfrastructure
{
    public EventCatchUpSubscriptionOptions Options { get; private set; } = new();

    void IEventCatchUpSubscriptionOptionsBuilderInfrastructure.WithEventDispatcherFactory(
        Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        Options = Options.WithEventDispatcherFactory(eventDispatcherFactory);
    }
}