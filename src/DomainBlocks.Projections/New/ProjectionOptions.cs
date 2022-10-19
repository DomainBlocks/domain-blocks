namespace DomainBlocks.Projections.New;

public abstract class ProjectionOptionsBase
{
    public ProjectionEventNameMap EventNameMap { get; } = new();

    public void WithDefaultEventName<TEvent>()
    {
        EventNameMap.RegisterDefaultEventName<TEvent>();
    }

    public abstract ProjectionRegistry ToProjectionRegistry();
}