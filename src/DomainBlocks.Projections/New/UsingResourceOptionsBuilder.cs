using System;

namespace DomainBlocks.Projections.New;

public class UsingResourceOptionsBuilder<TResource>
{
    public UsingResourceOptionsBuilder(
        EventCatchUpSubscriptionOptionsBuilder coreBuilder, Func<TResource> resourceFactory)
    {
        CoreBuilder = coreBuilder;
        ResourceFactory = resourceFactory;
    }

    public EventCatchUpSubscriptionOptionsBuilder CoreBuilder { get; }
    public Func<TResource> ResourceFactory { get; }
}