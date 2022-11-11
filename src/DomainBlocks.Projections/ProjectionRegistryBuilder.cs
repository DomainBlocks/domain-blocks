using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections;

public sealed class ProjectionRegistryBuilder
{
    private readonly IList<IEventProjectionBuilder> _eventProjectionBuilders = new List<IEventProjectionBuilder>();
    private ProjectionEventNameMap _eventNameMap = new();
    private ProjectionContextMap _projectionContextMap = new();

    public EventProjectionBuilder<TEvent> Event<TEvent>()
    {
        var builder = new EventProjectionBuilder<TEvent>(this);
        _eventProjectionBuilders.Add(builder);

        // Default event name to the .NET type.
        // This can be overridden by explicitly
        // specifying a name/names in the fluent builder
        _eventNameMap = _eventNameMap.RegisterDefaultEventName<TEvent>();

        return builder;
    }

    public ProjectionRegistry Build()
    {
        var eventProjectionMap = _eventProjectionBuilders
            .SelectMany(x => x.BuildProjectionFuncs())
            .Aggregate(new EventProjectionMap(), (acc, next) =>
            {
                var (eventType, func) = next;
                return acc.AddProjectionFunc(eventType, func);
            });

        return new ProjectionRegistry(_eventNameMap, eventProjectionMap, _projectionContextMap);
    }

    internal void OverrideEventNames<TEvent>(params string[] names)
    {
        if (names == null) throw new ArgumentNullException(nameof(names));
        if (names.Length == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(names));
        _eventNameMap = _eventNameMap.OverrideEventNames<TEvent>(names);
    }

    internal void RegisterContextForEvent<TEvent>(IProjectionContext projectionContext)
    {
        if (projectionContext == null) throw new ArgumentNullException(nameof(projectionContext));
        _projectionContextMap = _projectionContextMap.RegisterProjectionContext<TEvent>(projectionContext);
    }
}