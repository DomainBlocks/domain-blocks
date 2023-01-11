namespace DomainBlocks.Core.Projections;

public class ProjectionRegistry
{
    public ProjectionRegistry()
    {
    }

    public ProjectionRegistry(
        ProjectionEventNameMap eventNameMap,
        EventProjectionMap eventProjectionMap,
        ProjectionContextMap projectionContextMap)
    {
        EventNameMap = eventNameMap ?? throw new ArgumentNullException(nameof(eventNameMap));
        EventProjectionMap = eventProjectionMap ?? throw new ArgumentNullException(nameof(eventProjectionMap));
        ProjectionContextMap = projectionContextMap ?? throw new ArgumentNullException(nameof(projectionContextMap));
    }

    private ProjectionRegistry(ProjectionRegistry copyFrom)
    {
        EventNameMap = new ProjectionEventNameMap(copyFrom.EventNameMap);
        EventProjectionMap = new EventProjectionMap(copyFrom.EventProjectionMap);
        ProjectionContextMap = new ProjectionContextMap(copyFrom.ProjectionContextMap);
    }

    public ProjectionEventNameMap EventNameMap { get; private set; } = new();
    public EventProjectionMap EventProjectionMap { get; private set; } = new();
    public ProjectionContextMap ProjectionContextMap { get; private set; } = new();

    internal ProjectionRegistry RegisterDefaultEventName(Type eventType)
    {
        var copy = new ProjectionRegistry(this);
        copy.EventNameMap = copy.EventNameMap.RegisterDefaultEventName(eventType);
        return copy;
    }

    internal ProjectionRegistry AddProjectionFunc(Type eventType, RunProjection projectionFunc)
    {
        var copy = new ProjectionRegistry(this);
        copy.EventProjectionMap = copy.EventProjectionMap.AddProjectionFunc(eventType, projectionFunc);
        return copy;
    }

    internal ProjectionRegistry RegisterProjectionContext(Type eventType, IProjectionContext projectionContext)
    {
        var copy = new ProjectionRegistry(this);
        copy.ProjectionContextMap = copy.ProjectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        return copy;
    }
}