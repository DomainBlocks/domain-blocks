using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections;

public sealed class ProjectionContextMap
{
    private readonly HashSet<IProjectionContext> _allContexts = new();
    private readonly Dictionary<Type, HashSet<IProjectionContext>> _eventContextMap = new();

    public ProjectionContextMap()
    {
    }

    public ProjectionContextMap(ProjectionContextMap copyFrom)
    {
        _allContexts = new HashSet<IProjectionContext>(copyFrom._allContexts);

        _eventContextMap = copyFrom._eventContextMap
            .ToDictionary(x => x.Key, x => new HashSet<IProjectionContext>(x.Value));
    }

    public ProjectionContextMap RegisterProjectionContext<TEvent>(IProjectionContext projectionContext)
    {
        return RegisterProjectionContext(typeof(TEvent), projectionContext);
    }

    public ProjectionContextMap RegisterProjectionContext(Type eventType, IProjectionContext projectionContext)
    {
        if (projectionContext == null) throw new ArgumentNullException(nameof(projectionContext));

        var copy = new ProjectionContextMap(this);
        copy._allContexts.Add(projectionContext);

        if (copy._eventContextMap.TryGetValue(eventType, out var contexts))
        {
            contexts.Add(projectionContext);
        }
        else
        {
            copy._eventContextMap.Add(eventType, new HashSet<IProjectionContext> { projectionContext });
        }

        return copy;
    }

    public IEnumerable<IProjectionContext> GetAllContexts()
    {
        return _allContexts;
    }

    public IReadOnlyCollection<IProjectionContext> GetContextsForEventType(Type eventType)
    {
        if (eventType == null) throw new ArgumentNullException(nameof(eventType));
        if (_eventContextMap.TryGetValue(eventType, out var contexts))
        {
            return contexts;
        }

        return Array.Empty<IProjectionContext>();
    }
}