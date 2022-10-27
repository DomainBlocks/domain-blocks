using System;

namespace DomainBlocks.Projections.New;

public class UsingResourceOptionsBuilder<TResource> where TResource : IDisposable
{
    public UsingResourceOptionsBuilder(
        EventCatchUpSubscriptionOptionsBuilder coreBuilder, Func<TResource> resourceFactory)
    {
        CoreBuilder = coreBuilder;
        ResourceFactory = resourceFactory;
    }

    public EventCatchUpSubscriptionOptionsBuilder CoreBuilder { get; }
    public Func<TResource> ResourceFactory { get; }

    public WithServiceOptionsBuilder<TResource, TService> WithService<TService>(
        Func<TResource, TService> serviceFactory)
    {
        var initialOptions = new ServiceProjectionOptions<TResource, TService>()
            .WithResourceFactory(ResourceFactory)
            .WithServiceFactory(serviceFactory);

        return new WithServiceOptionsBuilder<TResource, TService>(CoreBuilder, initialOptions);
    }
}