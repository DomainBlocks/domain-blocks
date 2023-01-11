namespace DomainBlocks.Core.Projections;

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

    public bool TryGetProjections(Type eventType, out IReadOnlyCollection<RunProjection>? projections)
    {
        if (_projectionFuncs.TryGetValue(eventType, out var value))
        {
            projections = value.AsReadOnly();
            return true;
        }

        projections = null;
        return false;
    }
}