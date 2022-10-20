using System;

namespace DomainBlocks.Projections.New;

public class UsingResourceOptionsBuilder<TResource>
{
    public UsingResourceOptionsBuilder(
        EventCatchUpSubscriptionOptionsBuilder rootBuilder, Func<TResource> resourceFactory)
    {
        RootBuilder = rootBuilder;
        ResourceFactory = resourceFactory;
    }

    public EventCatchUpSubscriptionOptionsBuilder RootBuilder { get; }
    public Func<TResource> ResourceFactory { get; }
}