using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections.New;

public class EventCatchUpSubscriptionOptionsBuilder : IEventCatchUpSubscriptionOptionsBuilderInfrastructure
{
    private readonly List<IProjectionOptionsBuilder> _projectionOptionsBuilders = new();
    private EventCatchUpSubscriptionOptions _options = new();

    public EventCatchUpSubscriptionOptions Options
    {
        get
        {
            _options = _projectionOptionsBuilders
                .Aggregate(_options, (acc, next) => acc.AddProjectionOptions(next.Options));

            return _options;
        }
    }

    public EventCatchUpSubscriptionOptionsBuilder AddProjection(Action<ProjectionOptionsBuilder> optionsAction)
    {
        var projectionOptionsBuilder = new ProjectionOptionsBuilder();
        optionsAction(projectionOptionsBuilder);
        _options = Options.AddProjectionOptions(projectionOptionsBuilder.Options);
        return this;
    }

    public IStatefulProjectionOptionsBuilder<TState> WithState<TState>(Func<TState> stateFactory)
        where TState : IDisposable
    {
        var builder = new StatefulProjectionOptionsBuilder<TState, TState>(stateFactory, s => s);
        _projectionOptionsBuilders.Add(builder);
        return builder;
    }

    public StatefulProjectionOptionsBuilder<TResource> Using<TResource>(Func<TResource> resourceFactory)
        where TResource : IDisposable
    {
        var builder = new StatefulProjectionOptionsBuilder<TResource>(resourceFactory, this);
        return builder;
    }

    internal void AddProjectionOptionsBuilder(IProjectionOptionsBuilder builder)
    {
        _projectionOptionsBuilders.Add(builder);
    }

    void IEventCatchUpSubscriptionOptionsBuilderInfrastructure.WithEventDispatcherFactory(
        Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory)
    {
        _options = Options.WithEventDispatcherFactory(eventDispatcherFactory);
    }
}