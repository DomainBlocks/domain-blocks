using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections;

public sealed class EventProjectionMap
{
    private readonly Dictionary<Type, List<RunProjection>> _projectionFuncs = new();

    public EventProjectionMap()
    {
    }

    public EventProjectionMap(EventProjectionMap copyFrom)
    {
        _projectionFuncs = copyFrom._projectionFuncs.ToDictionary(x => x.Key, x => x.Value.ToList());
    }

    public EventProjectionMap AddProjectionFunc(Type eventType, RunProjection projectionFunc)
    {
        if (eventType == null) throw new ArgumentNullException(nameof(eventType));
        if (projectionFunc == null) throw new ArgumentNullException(nameof(projectionFunc));

        var copy = new EventProjectionMap(this);

        if (copy._projectionFuncs.TryGetValue(eventType, out var funcs))
        {
            funcs.Add(projectionFunc);
        }
        else
        {
            var projectionsList = new List<RunProjection> { projectionFunc };
            copy._projectionFuncs.Add(eventType, projectionsList);
        }

        return copy;
    }

    public bool TryGetValue(Type key, out IReadOnlyCollection<RunProjection> value)
    {
        if (_projectionFuncs.TryGetValue(key, out var valueImpl))
        {
            value = valueImpl.AsReadOnly();
            return true;
        }

        value = null;
        return false;
    }
}