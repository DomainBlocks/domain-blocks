using System;

namespace DomainBlocks.Projections.New;

public class WithServiceOptionsBuilder<TResource, TService> where TResource : IDisposable
{
    private readonly EventCatchUpSubscriptionOptionsBuilder _coreBuilder;
    private readonly ServiceProjectionOptions<TResource, TService> _initialOptions;

    public WithServiceOptionsBuilder(
        EventCatchUpSubscriptionOptionsBuilder coreBuilder,
        ServiceProjectionOptions<TResource, TService> initialOptions)
    {
        _coreBuilder = coreBuilder;
        _initialOptions = initialOptions;
    }

    public WithServiceOptionsBuilder<TResource, TService> AddProjection(
        Action<IServiceProjectionOptionsBuilder<TService>> optionsAction)
    {
        var builder = new ServiceProjectionOptionsBuilder<TResource, TService>(_initialOptions);
        optionsAction(builder);
        _coreBuilder.AddProjectionOptions(builder.Options);
        return this;
    }
}