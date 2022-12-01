using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class StatefulProjectionOptionsBuilder<TResource> where TResource : IDisposable
{
    private readonly Func<TResource> _resourceFactory;
    private readonly EventCatchUpSubscriptionOptionsBuilder _coreBuilder;

    public StatefulProjectionOptionsBuilder(
        Func<TResource> resourceFactory, EventCatchUpSubscriptionOptionsBuilder coreBuilder)
    {
        _resourceFactory = resourceFactory;
        _coreBuilder = coreBuilder;
    }

    public IStatefulProjectionOptionsBuilder<TState> WithState<TState>(
        Func<TResource, CatchUpSubscriptionStatus, TState> stateFactory)
    {
        var builder = new StatefulProjectionOptionsBuilder<TResource, TState>(_resourceFactory, stateFactory);
        _coreBuilder.AddProjectionOptionsBuilder(builder);
        return builder;
    }
}

public class StatefulProjectionOptionsBuilder<TResource, TState> :
    IProjectionOptionsBuilder,
    IStatefulProjectionOptionsBuilder<TState>
    where TResource : IDisposable
{
    private StatefulProjectionOptions<TResource, TState> _options;

    public StatefulProjectionOptionsBuilder(
        Func<TResource> resourceFactory, Func<TResource, CatchUpSubscriptionStatus, TState> stateFactory)
    {
        _options = new StatefulProjectionOptions<TResource, TState>()
            .WithResourceFactory(resourceFactory)
            .WithStateFactory(stateFactory);
    }

    public IProjectionOptions Options => _options;

    public IStatefulProjectionOptionsBuilder<TState> OnInitializing(
        Func<TState, CancellationToken, Task> onInitializing)
    {
        _options = _options.WithOnInitializing(onInitializing);
        return this;
    }

    public IStatefulProjectionOptionsBuilder<TState> OnSubscribing(
        Func<TState, CancellationToken, Task<IStreamPosition>> onSubscribing)
    {
        _options = _options.WithOnSubscribing(onSubscribing);
        return this;
    }

    public IStatefulProjectionOptionsBuilder<TState> OnSave(
        Func<TState, IStreamPosition, CancellationToken, Task> onSave)
    {
        _options = _options.WithOnSave(onSave);
        return this;
    }

    public IStatefulProjectionOptionsBuilder<TState> When<TEvent>(
        Func<TEvent, IEventHandlerContext<TState>, CancellationToken, Task> projection)
    {
        _options = _options.WithProjection(projection);
        return this;
    }

    public IStatefulProjectionOptionsBuilder<TState> When<TEvent>(
        Action<TEvent, IEventHandlerContext<TState>> projection)
    {
        _options = _options.WithProjection(projection);
        return this;
    }
}