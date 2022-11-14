using System;

namespace DomainBlocks.Projections.New;

public class UsingResourceOptionsBuilder<TResource> where TResource : IDisposable
{
    private readonly EventCatchUpSubscriptionOptionsBuilder _coreBuilder;
    private readonly Func<TResource> _resourceFactory;

    public UsingResourceOptionsBuilder(
        EventCatchUpSubscriptionOptionsBuilder coreBuilder, Func<TResource> resourceFactory)
    {
        _coreBuilder = coreBuilder;
        _resourceFactory = resourceFactory;
    }

    public WithServiceOptionsBuilder<TResource, TService> WithService<TService>(
        Func<TResource, TService> serviceFactory)
    {
        var initialOptions = new ServiceProjectionOptions<TResource, TService>()
            .WithResourceFactory(_resourceFactory)
            .WithServiceFactory(serviceFactory);

        return new WithServiceOptionsBuilder<TResource, TService>(_coreBuilder, initialOptions);
    }
}